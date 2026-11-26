using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.API.Controllers;

public sealed partial class TaskAttachmentController
{
    private sealed record LegacyScope(int TaskId, int UserId, TaskAttachmentKind Kind,
        int? MessageId, int? SubmissionId, int? SubTaskId, long? Version);

    private async Task<IActionResult> Upload(int taskId, IFormFile file, TaskAttachmentKind kind,
        int? messageId, int? submissionId, CancellationToken ct, int? subTaskId = null, long? version = null)
    {
        var scope = new LegacyScope(taskId, currentUser.UserId, kind, messageId, submissionId, subTaskId, version);
        if (!await collaboration.CanAccessAsync(taskId, scope.UserId, ct)) return Forbid();
        if (subTaskId.HasValue)
        {
            if (!SubTaskUploadsEnabled) return SubTaskUploadUnavailable();
            if (version is null or < 0) return BadRequest("A subtask version is required.");
            if (!await subTasks.HasActiveAsync(taskId, subTaskId.Value, ct)) return NotFound();
        }
        if (kind == TaskAttachmentKind.Message)
        {
            if (!DiscussionUploadsEnabled) return DiscussionUnavailable();
            if (!await CanAddLegacyDiscussionFileAsync(taskId, messageId!.Value, scope.UserId, ct))
                return BadRequest("Files can only be added separately to your own legacy message in this task.");
        }

        var fileLimit = kind switch
        {
            TaskAttachmentKind.Message => DiscussionFileLimit,
            TaskAttachmentKind.SubTaskSubmission => SubTaskFileLimit,
            _ => options.Value.MaxFileSizeBytes
        };
        var (upload, failure) = InspectUpload(file, fileLimit);
        if (failure is not null) return failure;

        await using var source = upload!.File.OpenReadStream();
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(source, ct));
        source.Position = 0;
        var candidates = LegacyTargetFiles(scope).Where(x => x.UploaderUserId == scope.UserId &&
            x.OriginalFileName == upload.Name && x.SizeBytes == file.Length);
        var oldMatch = kind == TaskAttachmentKind.Message ? await FindOldDiscussionMatchAsync(candidates, digest, ct) : null;
        var receipts = candidates.Where(x => x.ContentSha256 == digest || (oldMatch.HasValue && x.Id == oldMatch));
        if (await receipts.AnyAsync(ct))
        {
            var replay = await SaveLegacyFileAsync(scope, upload, digest, receipts, key: null, ct);
            if (replay is not null) return replay;
        }

        var eligibilityFailure = await CheckLegacySubmissionAsync(scope, ct);
        if (eligibilityFailure is not null) return eligibilityFailure;
        var scanFailure = await ScanUploadAsync(upload, source, requireClean: true, ct);
        if (scanFailure is not null) return scanFailure;

        var key = await store.SaveAsync(source, ct);
        return (await SaveLegacyFileAsync(scope, upload, digest, receipts, key, ct))!;
    }

    private async Task<IActionResult?> SaveLegacyFileAsync(LegacyScope scope, UploadFile upload, string digest,
        IQueryable<TaskAttachment> receipts, string? key, CancellationToken ct)
    {
        var (taskId, userId, kind, messageId, submissionId, subTaskId, version) = scope;
        var commitStarted = false;
        var committed = false;
        TaskAttachment attachment;
        int ownerId;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockSubTaskScopeAsync(taskId, ct, exclusiveParent: !subTaskId.HasValue);
            if (!await collaboration.CanAccessAsync(taskId, userId, ct)) return Forbid();
            var task = await db.TaskRequests.SingleAsync(x => x.Id == taskId && x.Activity, ct);
            ownerId = task.OwnerId;

            SubTask? child = null;
            if (subTaskId is int childId)
            {
                await subTasks.LockSubTaskForWriteAsync(taskId, childId, ct);
                child = await subTasks.GetActiveAsync(taskId, childId, ct);
                if (child is null) return NotFound();
            }
            if (kind == TaskAttachmentKind.Message && !await CanAddLegacyDiscussionFileAsync(taskId, messageId!.Value, userId, ct))
                return BadRequest("Files can only be added separately to your own legacy message in this task.");

            var replay = await receipts.Include(x => x.UploaderUser).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (replay is not null) return Ok(Map(replay, userId, ownerId));
            if (key is null) return null;
            if (child is not null && child.Version != version)
                return Conflict("The submission or subtask changed. Reload the current revision before uploading.");
            var eligibilityFailure = await CheckLegacySubmissionAsync(scope, ct);
            if (eligibilityFailure is not null) return eligibilityFailure;

            if (kind == TaskAttachmentKind.Message || child is not null)
            {
                var maxFiles = child is not null ? SubTaskAttachmentLimit : DiscussionAttachmentLimit;
                var sizes = await LegacyTargetFiles(scope).Select(x => x.SizeBytes).Take(maxFiles).ToListAsync(ct);
                var total = sizes.Sum() + upload.File.Length;
                var quotaFailure = child is not null ? CheckSubTaskQuota(sizes.Count + 1, total) :
                    CheckDiscussionQuota(sizes.Count + 1, total);
                if (quotaFailure is not null) return quotaFailure;
            }

            var uploader = await db.Users.SingleAsync(x => x.Id == userId, ct);
            attachment = new TaskAttachment
            {
                TaskRequestId = taskId, UploaderUserId = userId, UploaderUser = uploader, Kind = kind,
                TaskMessageId = messageId,
                TaskSubmissionId = kind == TaskAttachmentKind.Submission ? submissionId : null,
                SubTaskSubmissionId = child is not null ? submissionId : null,
                StorageKey = key, OriginalFileName = upload.Name, ContentType = upload.Mime,
                SizeBytes = upload.File.Length, ContentSha256 = digest
            };
            if (child is not null)
                db.Entry(child).Property(x => x.Version).IsModified = true;
            if (kind == TaskAttachmentKind.Submission)
                db.Entry(task).Property(x => x.Version).IsModified = true;
            db.TaskAttachments.Add(attachment);
            await db.SaveChangesAsync(ct);
            commitStarted = true;
            await transaction.CommitAsync(CancellationToken.None);
            committed = true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("The task changed before the upload finished. Check the current revision and try again.");
        }
        finally
        {
            if (!committed && key is not null) await CleanupUploadAsync(taskId, [key], commitStarted);
        }

        if (kind is TaskAttachmentKind.Submission or TaskAttachmentKind.SubTaskSubmission)
            await collaboration.PublishTaskChangedAsync(taskId);
        if (subTaskId.HasValue)
            return CreatedAtAction(nameof(DownloadSubTaskFile), new { taskId, subTaskId, submissionId, id = attachment.Id },
                Map(attachment, userId, ownerId));
        await collaboration.PublishAttachmentsChangedAsync(taskId);
        return CreatedAtAction(nameof(Download), new { id = attachment.Id }, Map(attachment, userId, ownerId));
    }

    private IQueryable<TaskAttachment> LegacyTargetFiles(LegacyScope scope) => db.TaskAttachments.AsNoTracking()
        .Where(x => x.TaskRequestId == scope.TaskId && x.Kind == scope.Kind &&
            x.TaskMessageId == scope.MessageId && !x.IsDeleted &&
            (scope.SubTaskId.HasValue
                ? x.SubTaskSubmissionId == scope.SubmissionId && x.SubTaskSubmission!.SubTaskId == scope.SubTaskId
                : x.TaskSubmissionId == scope.SubmissionId));

    private async Task<IActionResult?> CheckLegacySubmissionAsync(LegacyScope scope, CancellationToken ct)
    {
        if (scope.SubTaskId is int childId &&
            !await CanUploadSubTaskFileAsync(scope.TaskId, childId, scope.SubmissionId!.Value, scope.UserId, ct))
            return Conflict("Files can only be attached to your current unreviewed subtask submission.");
        if (scope.Kind == TaskAttachmentKind.Submission &&
            !await db.TaskSubmissions.AnyAsync(x => x.Id == scope.SubmissionId &&
                x.TaskRequestId == scope.TaskId && x.SubmittedByUserId == scope.UserId &&
                x.TaskRequest.Status == TaskStatus.InReview && x.TaskRequest.AssigneeUserId == scope.UserId &&
                !db.TaskSubmissionReviews.Any(r => r.TaskSubmissionId == x.Id) &&
                !db.TaskSubmissions.Any(newer => newer.TaskRequestId == scope.TaskId && newer.RevisionNumber > x.RevisionNumber), ct))
            return Conflict("Files can only be attached to your current unreviewed submission revision.");
        return null;
    }

    private async Task<int?> FindOldDiscussionMatchAsync(IQueryable<TaskAttachment> candidates, string digest, CancellationToken ct)
    {
        var historical = await candidates.Where(x => x.ContentSha256 == null)
            .Select(x => new { x.Id, x.StorageKey }).ToListAsync(ct);
        foreach (var candidate in historical)
        {
            try
            {
                await using var saved = store.OpenRead(candidate.StorageKey);
                if (Convert.ToHexString(await SHA256.HashDataAsync(saved, ct)) == digest) return candidate.Id;
            }
            catch (IOException)
            {
                // An unreadable historical blob cannot acknowledge a retry.
            }
        }
        return null;
    }
}
