using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.API.Controllers;

public sealed partial class TaskAttachmentController
{
    private const long SubTaskBytesCeiling = 100L * 1024 * 1024;
    private const long SubTaskRequestLimit = SubTaskBytesCeiling + 1024 * 1024;
    private int SubTaskAttachmentLimit => Math.Clamp(options.Value.MaxSubTaskSubmissionAttachments, 0, SubTaskSubmission.MaxDeliveryFileCount);
    private long SubTaskTotalLimit => Math.Clamp(options.Value.MaxSubTaskSubmissionUploadBytes, 0, SubTaskBytesCeiling);
    private long SubTaskFileLimit => Math.Min(options.Value.MaxFileSizeBytes, SubTaskTotalLimit);

    public sealed class SubTaskDeliveryForm
    {
        public long Version { get; set; } = -1;
        public Guid ClientRequestId { get; set; }
        public string? Content { get; set; }
        public List<IFormFile> Files { get; set; } = [];
    }

    [HttpPost("/api/TaskRequest/{taskId:int}/subtasks/{subTaskId:int}/submissions/delivery")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SubTaskRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = SubTaskRequestLimit)]
    public async Task<IActionResult> DeliverSubTaskSubmission(
        int taskId, int subTaskId, [FromForm] SubTaskDeliveryForm form, CancellationToken ct)
    {
        if (taskId <= 0 || subTaskId <= 0)
            return BadRequest("Resource identifiers must be positive.");
        if (form.Version < 0)
            return BadRequest("A nonnegative subtask version is required.");
        if (form.ClientRequestId == Guid.Empty)
            return BadRequest("A delivery identifier is required.");

        var content = form.Content?.Trim() ?? string.Empty;
        if (content.Length > SubTaskSubmission.MaxContentLength)
            return BadRequest($"A submission must contain 1–{SubTaskSubmission.MaxContentLength} characters.");
        if (content.Length == 0 && form.Files.Count == 0)
            return BadRequest("Write a delivery summary or attach at least one file.");
        if (form.Files.Count > SubTaskSubmission.MaxDeliveryFileCount)
            return BadRequest($"Attach at most {SubTaskSubmission.MaxDeliveryFileCount} files per submission.");

        var uploads = new List<UploadFile>(form.Files.Count);
        foreach (var file in form.Files)
        {
            var (upload, failure) = InspectUpload(file, SubTaskBytesCeiling);
            if (failure is not null) return failure;
            uploads.Add(upload!);
        }

        var totalBytes = uploads.Sum(x => x.File.Length);
        if (totalBytes > SubTaskBytesCeiling)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, "The files exceed the maximum submission size.");

        var identities = new List<DiscussionFileIdentity>(uploads.Count);
        foreach (var upload in uploads)
        {
            await using var source = upload.File.OpenReadStream();
            identities.Add(new(upload.Name, upload.File.Length, Convert.ToHexString(await SHA256.HashDataAsync(source, ct))));
        }

        if (identities.Distinct().Count() != uploads.Count)
            return BadRequest("Remove duplicate files before submitting.");

        var fingerprint = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { content, identities })));

        var userId = currentUser.UserId;
        await using (var read = await db.Database.BeginTransactionAsync(ct))
        {
            await LockSubTaskScopeAsync(taskId, ct);
            if (!await collaboration.CanAccessAsync(taskId, userId, ct))
                return Forbid();
            var parent = await subTasks.GetActiveParentAsync(taskId, ct);
            if (parent is null)
                return NotFound();
            await subTasks.LockSubTaskForReadAsync(taskId, subTaskId, ct);
            var subTask = await subTasks.GetActiveAsync(taskId, subTaskId, ct);
            if (subTask is null)
                return NotFound();

            var previous = await FindSubTaskDeliveryAsync(taskId, subTaskId, userId, form.ClientRequestId, ct);
            if (previous is not null)
            {
                var receipt = await SubTaskDeliveryReceiptAsync(taskId, subTaskId, userId, previous, fingerprint, ct);
                await read.CommitAsync(ct);
                return receipt;
            }

            if (subTask.AssigneeUserId != userId || parent.OwnerId == userId ||
                !await IsEligibleSubTaskAssigneeAsync(parent, userId, ct))
                return Forbid();
            if (IsSubTaskParentLocked(parent))
                return Conflict(new { code = "conflict", message = "The parent task is locked for subtask changes." });
            if (subTask.Version != form.Version)
                return Conflict(new { code = "conflict", message = "The subtask changed. Refresh and retry." });
            if (subTask.Status != TaskStatus.InProgress)
                return Conflict(new { code = "conflict", message = "The subtask is in a state that does not allow this action." });

            await read.CommitAsync(ct);
        }

        db.ChangeTracker.Clear();

        if (uploads.Count > 0)
        {
            if (!SubTaskUploadsEnabled)
                return SubTaskUploadUnavailable();
            if (uploads.Count > SubTaskAttachmentLimit)
                return BadRequest($"Attach at most {SubTaskAttachmentLimit} files per submission.");
            if (totalBytes > SubTaskTotalLimit)
                return StatusCode(StatusCodes.Status413PayloadTooLarge, "The files exceed the configured submission size limit.");
            var oversized = uploads.FirstOrDefault(x => x.File.Length > SubTaskFileLimit);
            if (oversized is not null)
                return StatusCode(StatusCodes.Status413PayloadTooLarge, $"'{oversized.Name}' exceeds the configured file size limit.");
        }

        var staged = new List<(UploadFile File, string Key)>(uploads.Count);
        var commitAttempted = false;
        SubTaskDto? committedDto = null;
        int ownerId = 0;

        try
        {
            foreach (var upload in uploads)
            {
                await using var source = upload.File.OpenReadStream();
                var failure = await ScanUploadAsync(upload, source, requireClean: true, ct);
                if (failure is not null) return failure;
                staged.Add((upload, await store.SaveAsync(source, ct)));
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockSubTaskScopeAsync(taskId, ct);
            if (!await collaboration.CanAccessAsync(taskId, userId, ct))
                return Forbid();
            var parent = await subTasks.GetActiveParentAsync(taskId, ct);
            if (parent is null)
                return NotFound();
            var subTask = await subTasks.GetActiveAsync(taskId, subTaskId, ct);
            if (subTask is null)
                return NotFound();

            var previous = await FindSubTaskDeliveryAsync(taskId, subTaskId, userId, form.ClientRequestId, ct);
            if (previous is not null)
                return await SubTaskDeliveryReceiptAsync(taskId, subTaskId, userId, previous, fingerprint, ct);

            if (subTask.AssigneeUserId != userId || parent.OwnerId == userId ||
                !await IsEligibleSubTaskAssigneeAsync(parent, userId, ct))
                return Forbid();
            if (IsSubTaskParentLocked(parent))
                return Conflict(new { code = "conflict", message = "The parent task is locked for subtask changes." });
            if (subTask.Version != form.Version)
                return Conflict(new { code = "conflict", message = "The subtask changed. Refresh and retry." });
            if (subTask.Status != TaskStatus.InProgress)
                return Conflict(new { code = "conflict", message = "The subtask is in a state that does not allow this action." });

            ownerId = parent.OwnerId;
            var latest = await subTasks.GetLatestSubmissionAsync(taskId, subTaskId);
            var submitter = await db.Users.SingleAsync(x => x.Id == userId, ct);
            var now = DateTime.UtcNow;

            var submission = new SubTaskSubmission
            {
                SubTask = subTask,
                SubTaskId = subTask.Id,
                TaskRequestId = taskId,
                SubmittedByUserId = userId,
                SubmittedByUser = submitter,
                RevisionNumber = checked((latest?.RevisionNumber ?? 0) + 1),
                Content = content,
                ClientRequestId = form.ClientRequestId,
                DeliveryFingerprint = fingerprint,
                DeliveryFileCount = staged.Count,
                CreatedAt = now
            };
            db.SubTaskSubmissions.Add(submission);

            foreach (var (upload, key) in staged)
            {
                db.TaskAttachments.Add(new TaskAttachment
                {
                    TaskRequestId = taskId,
                    UploaderUserId = userId,
                    UploaderUser = submitter,
                    Kind = TaskAttachmentKind.SubTaskSubmission,
                    SubTaskSubmission = submission,
                    OriginalFileName = upload.Name,
                    ContentType = upload.Mime,
                    SizeBytes = upload.File.Length,
                    StorageKey = key,
                    CreatedAt = now
                });
            }

            var previousStatus = subTask.Status;
            subTask.Status = TaskStatus.InReview;
            db.SubTaskActivities.Add(new SubTaskActivity
            {
                TaskRequestId = taskId,
                SubTaskId = subTask.Id,
                SubTask = subTask,
                ActorUserId = userId,
                ActivityType = TaskActivityType.SubmissionCreated,
                FromStatus = previousStatus,
                ToStatus = TaskStatus.InReview,
                Submission = submission,
                CreatedAt = now
            });

            await db.SaveChangesAsync(ct);
            committedDto = await subTasks.GetDetailAsync(taskId, subTaskId, userId, ct);
            commitAttempted = true;
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsSubTaskDeliveryConflict(ex))
        {
            db.ChangeTracker.Clear();
            var committed = await FindSubTaskDeliveryAsync(taskId, subTaskId, userId, form.ClientRequestId, CancellationToken.None);
            if (committed is not null)
                return await SubTaskDeliveryReceiptAsync(taskId, subTaskId, userId, committed, fingerprint, CancellationToken.None);

            return Conflict(new { code = "conflict", message = "The subtask changed. Refresh and retry." });
        }
        finally
        {
            if (!commitAttempted)
            {
                foreach (var (_, key) in staged)
                    RemoveStagedDiscussionFile(key);
            }
        }

        await collaboration.PublishTaskChangedAsync(taskId);
        if (notifications is not null && ownerId > 0)
        {
            try
            {
                await notifications.CreateTaskNotificationAsync(
                    ownerId,
                    NotificationType.TaskUpdated,
                    "Subtask awaiting review",
                    "A subtask submission is ready for your review.",
                    taskId,
                    $"/tasks/task-detail/{taskId}?subtask={subTaskId}");
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Subtask submission notification failed for subtask {SubTaskId}", subTaskId);
            }
        }

        return Ok(committedDto);
    }

    private Task<SubTaskSubmission?> FindSubTaskDeliveryAsync(int taskId, int subTaskId, int userId, Guid requestId,
        CancellationToken ct) =>
        db.SubTaskSubmissions.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TaskRequestId == taskId &&
            x.SubTaskId == subTaskId &&
            x.SubmittedByUserId == userId &&
            x.ClientRequestId == requestId, ct);

    private async Task<IActionResult> SubTaskDeliveryReceiptAsync(int taskId, int subTaskId, int userId,
        SubTaskSubmission previous, string fingerprint, CancellationToken ct) =>
        previous.DeliveryFingerprint == fingerprint
            ? Ok(await subTasks.GetDetailAsync(taskId, subTaskId, userId, ct))
            : Conflict(new
            {
                code = "delivery_conflict",
                message = "This delivery identifier was already used for a different submission."
            });

    private IActionResult SubTaskUploadUnavailable() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { code = "subtask_upload_unavailable", message = "Subtask delivery file uploads are unavailable." });

    private Task<bool> IsEligibleSubTaskAssigneeAsync(TaskRequest parent, int userId, CancellationToken ct) =>
        parent.WorkspaceId.HasValue
            ? db.WorkspaceMembers.AnyAsync(m =>
                m.WorkspaceId == parent.WorkspaceId.Value && m.UserId == userId && m.IsActive, ct)
            : parent.OwnerId == userId
                ? Task.FromResult(true)
                : db.TaskShares.AnyAsync(s =>
                    s.TaskRequestId == parent.Id &&
                    s.SharedWithUserId == userId &&
                    s.Permission >= TaskPermission.Edit &&
                    s.Permission <= TaskPermission.Manage, ct);

    private static bool IsSubTaskParentLocked(TaskRequest parent) =>
        parent.Status is TaskStatus.Completed or TaskStatus.Cancelled or TaskStatus.InReview;

    private static bool IsSubTaskDeliveryConflict(Exception ex) =>
        ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } ||
        ex is DbUpdateException && ex.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
}
