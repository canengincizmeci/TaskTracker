using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.API.Controllers;

[ApiController, Route("api/[controller]"), Authorize(Roles = "Admin,User")]
public sealed partial class TaskAttachmentController(
    TaskTrackerDbContext db, ICurrentUserService currentUser, ITaskCollaborationService collaboration,
    ITaskFileStore store, ITaskFileScanner scanner, IOptions<TaskFileOptions> options, ISubTaskDal subTasks,
    ILogger<TaskAttachmentController>? logger = null) : ControllerBase
{
    private bool ScannerConfigured => scanner is not NoOpTaskFileScanner &&
        (scanner is not ClamAvTaskFileScanner clamAv || clamAv.IsConfigured);
    private bool SubTaskUploadsEnabled => options.Value.EnableSubTaskSubmissionUploads && ScannerConfigured;
    private static readonly Dictionary<string, (string Mime, Func<byte[], bool> Signature)> Allowed =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ("application/pdf", b => Starts(b, "%PDF-"u8.ToArray())),
            [".png"] = ("image/png", b => Starts(b, new byte[] {137,80,78,71,13,10,26,10})),
            [".jpg"] = ("image/jpeg", IsJpeg), [".jpeg"] = ("image/jpeg", IsJpeg),
            [".webp"] = ("image/webp", b => b.Length >= 12 && Starts(b, "RIFF"u8.ToArray()) && b.AsSpan(8,4).SequenceEqual("WEBP"u8)),
            [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", IsZip),
            [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", IsZip),
            [".pptx"] = ("application/vnd.openxmlformats-officedocument.presentationml.presentation", IsZip),
            [".zip"] = ("application/zip", IsZip),
            [".txt"] = ("text/plain", IsText), [".csv"] = ("text/csv", IsText)
        };

    [HttpGet("task/{taskId:int}")]
    [Authorize(Roles = "User")]
    public async Task<IActionResult> List(int taskId)
    {
        var userId = currentUser.UserId;
        if (!await collaboration.CanAccessAsync(taskId, userId)) return Forbid();
        var ownerId = await db.TaskRequests.Where(x => x.Id == taskId).Select(x => x.OwnerId).SingleAsync();
        var entities = await ParentAttachments().AsNoTracking().Where(x => x.TaskRequestId == taskId)
            .Include(x => x.UploaderUser).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .ToListAsync();
        return Ok(entities.Select(x => Map(x, userId, ownerId)));
    }

    [HttpPost("task/{taskId:int}"), RequestSizeLimit(21 * 1024 * 1024)]
    [Authorize(Roles = "User")]
    public Task<IActionResult> UploadTask(int taskId, IFormFile file, CancellationToken ct) =>
        Upload(taskId, file, TaskAttachmentKind.Task, null, null, ct);

    [HttpPost("task/{taskId:int}/messages/{messageId:int}"), RequestSizeLimit(21 * 1024 * 1024)]
    [Authorize(Roles = "User")]
    public Task<IActionResult> UploadMessage(int taskId, int messageId, IFormFile file, CancellationToken ct) =>
        Upload(taskId, file, TaskAttachmentKind.Message, messageId, null, ct);

    [HttpPost("task/{taskId:int}/submissions/{submissionId:int}"), RequestSizeLimit(21 * 1024 * 1024)]
    [Authorize(Roles = "User")]
    public Task<IActionResult> UploadSubmission(int taskId, int submissionId, IFormFile file, CancellationToken ct) =>
        Upload(taskId, file, TaskAttachmentKind.Submission, null, submissionId, ct);

    [HttpGet("/api/TaskRequest/{taskId:int}/subtasks/{subTaskId:int}/submission-files")]
    public async Task<IActionResult> ListSubTaskFiles(int taskId, int subTaskId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockSubTaskScopeAsync(taskId, ct);
        if (!await collaboration.CanAccessAsync(taskId, userId, ct)) return Forbid();
        await subTasks.LockSubTaskForReadAsync(taskId, subTaskId, ct);
        if (!await subTasks.HasActiveAsync(taskId, subTaskId, ct)) return NotFound();
        var item = await subTasks.GetDetailAsync(taskId, subTaskId, userId, ct);
        var files = await SubTaskFiles(taskId, subTaskId).Include(x => x.UploaderUser)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        int? uploadSubmissionId = SubTaskUploadsEnabled && item.CurrentSubmissionId is int submissionId &&
            await CanUploadSubTaskFileAsync(taskId, subTaskId, submissionId, userId, ct) ? submissionId : null;
        await transaction.CommitAsync(ct);
        return Ok(new SubTaskFilesDto(item.Version, SubTaskUploadsEnabled, uploadSubmissionId,
            files.Select(x => Map(x, userId, ownerId: 0)).ToList()));
    }

    [HttpPost("/api/TaskRequest/{taskId:int}/subtasks/{subTaskId:int}/submissions/{submissionId:int}/files")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    public Task<IActionResult> UploadSubTaskFile(int taskId, int subTaskId, int submissionId, [FromQuery] long? version,
        IFormFile file, CancellationToken ct) =>
        Upload(taskId, file, TaskAttachmentKind.SubTaskSubmission, null, submissionId, ct, subTaskId, version);

    [HttpGet("/api/TaskRequest/{taskId:int}/subtasks/{subTaskId:int}/submissions/{submissionId:int}/files/{id:int}/download")]
    public async Task<IActionResult> DownloadSubTaskFile(int taskId, int subTaskId, int submissionId, int id,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockSubTaskScopeAsync(taskId, ct);
        if (!await collaboration.CanAccessAsync(taskId, currentUser.UserId, ct)) return Forbid();
        await subTasks.LockSubTaskForReadAsync(taskId, subTaskId, ct);
        var attachment = await SubTaskFiles(taskId, subTaskId)
            .SingleOrDefaultAsync(x => x.Id == id && x.SubTaskSubmissionId == submissionId, ct);
        if (attachment is null) return NotFound();
        await transaction.CommitAsync(ct);
        return StreamAttachment(attachment);
    }

    [HttpGet("{id:int}/download")]
    [Authorize(Roles = "User")]
    public async Task<IActionResult> Download(int id)
    {
        var userId = currentUser.UserId;
        var attachment = await ParentAttachments().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (attachment is null) return NotFound();
        if (!await collaboration.CanAccessAsync(attachment.TaskRequestId, userId)) return Forbid();
        return StreamAttachment(attachment);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "User")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = currentUser.UserId;
        var attachment = await ParentAttachments().Include(x => x.TaskRequest).SingleOrDefaultAsync(x => x.Id == id);
        if (attachment is null) return NotFound();
        if (!await collaboration.CanAccessAsync(attachment.TaskRequestId, userId)) return Forbid();
        if (attachment.Kind != TaskAttachmentKind.Task) return Conflict("Discussion and submission evidence is immutable.");
        if (attachment.UploaderUserId != userId && attachment.TaskRequest.OwnerId != userId) return Forbid();
        attachment.IsDeleted = true; attachment.DeletedAt = DateTime.UtcNow; attachment.DeletedByUserId = userId;
        await db.SaveChangesAsync();
        store.Delete(attachment.StorageKey);
        await collaboration.PublishAttachmentsChangedAsync(attachment.TaskRequestId);
        return NoContent();
    }

    private IQueryable<TaskAttachment> ParentAttachments() => db.TaskAttachments
        .Where(x => !x.IsDeleted && x.Kind != TaskAttachmentKind.SubTaskSubmission);

    private async Task<IActionResult> Upload(int taskId, IFormFile file, TaskAttachmentKind kind,
        int? messageId, int? submissionId, CancellationToken ct, int? subTaskId = null, long? version = null)
    {
        var userId = currentUser.UserId;
        if (!await collaboration.CanAccessAsync(taskId, userId, ct)) return Forbid();
        if (subTaskId is int childId)
        {
            if (!SubTaskUploadsEnabled) return StatusCode(503, "Subtask delivery file uploads are unavailable.");
            if (version is null or < 0) return BadRequest("A subtask version is required.");
            if (!await subTasks.HasActiveAsync(taskId, childId, ct)) return NotFound();
            if (!await CanUploadSubTaskFileAsync(taskId, childId, submissionId!.Value, userId, ct))
                return Conflict("Files can only be attached to your current unreviewed subtask submission.");
        }
        if (kind == TaskAttachmentKind.Message && !DiscussionUploadsEnabled) return DiscussionUnavailable();
        if (kind == TaskAttachmentKind.Message && !await CanAddLegacyDiscussionFileAsync(taskId, messageId!.Value, userId, ct))
            return BadRequest("Files can only be added separately to your own legacy message in this task.");
        var (upload, failure) = InspectUpload(file,
            kind == TaskAttachmentKind.Message ? DiscussionFileLimit : options.Value.MaxFileSizeBytes);
        if (failure is not null) return failure;
        await using var source = upload!.File.OpenReadStream();
        var scanFailure = await ScanUploadAsync(upload, source, requireClean: true, ct);
        if (scanFailure is not null) return scanFailure;
        var name = upload.Name;
        if (kind == TaskAttachmentKind.Message)
            return await SaveLegacyDiscussionFileAsync(taskId, messageId!.Value, userId, upload, source, ct);
        if (subTaskId.HasValue)
            return await SaveSubTaskFileAsync(taskId, subTaskId.Value, submissionId!.Value, version!.Value,
                userId, source, name, upload.Mime, file.Length, ct);
        if (!await collaboration.CanAccessAsync(taskId, userId, ct)) return Forbid();
        var task = await db.TaskRequests.SingleOrDefaultAsync(x => x.Id == taskId && x.Activity, ct);
        if (task is null) return Forbid();
        if (kind == TaskAttachmentKind.Submission)
        {
            var validSubmission = task.Status == TaskStatus.InReview && task.AssigneeUserId == userId &&
                await db.TaskSubmissions.AnyAsync(x => x.Id == submissionId &&
                x.TaskRequestId == taskId && x.SubmittedByUserId == userId &&
                !db.TaskSubmissionReviews.Any(r => r.TaskSubmissionId == x.Id) &&
                !db.TaskSubmissions.Any(newer => newer.TaskRequestId == taskId && newer.RevisionNumber > x.RevisionNumber), ct);
            if (!validSubmission) return Conflict("Files can only be attached to your current unreviewed submission revision.");
        }

        var uploader = await db.Users.SingleAsync(x => x.Id == userId, ct);
        var key = await store.SaveAsync(source, ct);
        var entity = new TaskAttachment { TaskRequestId = taskId, UploaderUserId = userId, UploaderUser = uploader, Kind = kind,
            TaskSubmissionId = submissionId, OriginalFileName = name,
            StorageKey = key, ContentType = upload.Mime, SizeBytes = file.Length, CreatedAt = DateTime.UtcNow };
        try
        {
            if (!await collaboration.CanAccessAsync(taskId, userId, ct))
            {
                store.Delete(key);
                return Forbid();
            }
            if (kind == TaskAttachmentKind.Submission)
                db.Entry(task).Property(x => x.Version).IsModified = true;
            db.TaskAttachments.Add(entity);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            store.Delete(key);
            return Conflict("The task changed before the upload finished. Check the current revision and try again.");
        }
        catch { store.Delete(key); throw; }

        if (kind == TaskAttachmentKind.Submission) await collaboration.PublishTaskChangedAsync(taskId);
        await collaboration.PublishAttachmentsChangedAsync(taskId);
        return CreatedAtAction(nameof(Download), new { id = entity.Id }, Map(entity, userId, task.OwnerId));
    }

    private sealed record UploadFile(IFormFile File, string Name, string Mime, Func<byte[], bool> Signature);

    private (UploadFile? File, IActionResult? Error) InspectUpload(IFormFile? file, long maxSize)
    {
        if (file is null || file.Length <= 0) return (null, BadRequest("Choose a non-empty file."));
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl))
            return (null, BadRequest("The filename is invalid or too long."));
        if (file.Length > maxSize) return (null, StatusCode(413, $"'{name}' exceeds the configured file size limit."));
        if (!Allowed.TryGetValue(Path.GetExtension(name.TrimEnd()), out var rule))
            return (null, BadRequest($"The file type for '{name}' is not allowed."));
        return (new UploadFile(file, name, rule.Mime, rule.Signature), null);
    }

    private async Task<IActionResult?> ScanUploadAsync(UploadFile upload, Stream source, bool requireClean, CancellationToken ct)
    {
        var header = new byte[(int)Math.Min(512, upload.File.Length)];
        var read = await source.ReadAtLeastAsync(header.AsMemory(), header.Length, false, ct);
        if (!upload.Signature(header[..read]))
            return BadRequest($"The content of '{upload.Name}' does not match its extension.");
        source.Position = 0;
        TaskFileScanVerdict verdict;
        try { verdict = await scanner.ScanAsync(source, upload.Name, ct); }
        catch (Exception ex) when (ex is IOException or SocketException or HttpRequestException or TimeoutException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "File scanner could not verify an attachment");
            verdict = TaskFileScanVerdict.NotConfigured;
            requireClean = true;
        }
        if (verdict == TaskFileScanVerdict.Rejected)
            return BadRequest($"'{upload.Name}' was rejected by the configured security scanner.");
        if ((options.Value.RequireCleanScan || requireClean || ScannerConfigured) && verdict != TaskFileScanVerdict.Clean)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { code = "file_scan_unavailable", message = "File scanning is unavailable. Try again later." });
        source.Position = 0;
        return null;
    }

    private async Task<IActionResult> SaveSubTaskFileAsync(int taskId, int subTaskId, int submissionId, long version,
        int userId, Stream source, string name, string mime, long size, CancellationToken ct)
    {
        var key = await store.SaveAsync(source, ct);
        var commitAttempted = false;
        TaskAttachment attachment;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockSubTaskScopeAsync(taskId, ct);
            if (!await collaboration.CanAccessAsync(taskId, userId, ct)) return Forbid();
            var subTask = await subTasks.GetActiveAsync(taskId, subTaskId, ct);
            if (subTask is null) return NotFound();
            if (subTask.Version != version || !await CanUploadSubTaskFileAsync(taskId, subTaskId, submissionId, userId, ct))
                return Conflict("The submission or subtask changed. Reload the current revision before uploading.");
            var uploader = await db.Users.SingleAsync(x => x.Id == userId, ct);
            attachment = new TaskAttachment
            {
                TaskRequestId = taskId, UploaderUserId = userId, UploaderUser = uploader,
                Kind = TaskAttachmentKind.SubTaskSubmission, SubTaskSubmissionId = submissionId,
                StorageKey = key, OriginalFileName = name, ContentType = mime, SizeBytes = size
            };
            db.Entry(subTask).Property(x => x.Version).IsModified = true;
            db.TaskAttachments.Add(attachment);
            await db.SaveChangesAsync(ct);
            // A lost commit response may still leave a valid DB reference to the private file.
            commitAttempted = true;
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("The subtask changed before the file was saved.");
        }
        finally
        {
            if (!commitAttempted) store.Delete(key);
        }
        await collaboration.PublishTaskChangedAsync(taskId);
        return CreatedAtAction(nameof(DownloadSubTaskFile), new { taskId, subTaskId, submissionId, id = attachment.Id },
            Map(attachment, userId, ownerId: 0));
    }

    private IQueryable<TaskAttachment> SubTaskFiles(int taskId, int subTaskId) => db.TaskAttachments.AsNoTracking()
        .Where(x => x.TaskRequestId == taskId && x.Kind == TaskAttachmentKind.SubTaskSubmission && !x.IsDeleted &&
            x.SubTaskSubmission != null && x.SubTaskSubmission.SubTaskId == subTaskId && x.SubTaskSubmission.SubTask.Activity);

    private Task<bool> CanUploadSubTaskFileAsync(int taskId, int subTaskId, int submissionId, int userId, CancellationToken ct) =>
        db.SubTaskSubmissions.AnyAsync(x => x.Id == submissionId && x.TaskRequestId == taskId && x.SubTaskId == subTaskId &&
            x.SubmittedByUserId == userId && x.SubTask.Activity && x.SubTask.Status == TaskStatus.InReview &&
            x.SubTask.AssigneeUserId == userId && x.SubTask.TaskRequest.OwnerId != userId &&
            (x.SubTask.TaskRequest.Status == TaskStatus.Pending || x.SubTask.TaskRequest.Status == TaskStatus.InProgress) &&
            (x.SubTask.TaskRequest.WorkspaceId != null ? x.SubTask.TaskRequest.Workspace!.Members.Any(m => m.IsActive && m.UserId == userId)
                : x.SubTask.TaskRequest.TaskShares.Any(s => s.SharedWithUserId == userId &&
                    s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage)) &&
            !db.SubTaskSubmissionReviews.Any(r => r.SubTaskSubmissionId == x.Id) &&
            !db.SubTaskSubmissions.Any(s => s.SubTaskId == subTaskId && s.RevisionNumber > x.RevisionNumber), ct);

    private async Task LockSubTaskScopeAsync(int taskId, CancellationToken ct = default)
    {
        await subTasks.LockParentWorkspaceAsync(taskId, ct);
        await subTasks.LockParentAsync(taskId, exclusive: false, ct);
    }

    private FileStreamResult StreamAttachment(TaskAttachment attachment)
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox";
        return File(store.OpenRead(attachment.StorageKey), "application/octet-stream", attachment.OriginalFileName,
            enableRangeProcessing: true);
    }

    private static TaskAttachmentDto Map(TaskAttachment x, int userId, int ownerId) => new()
    { Id=x.Id, TaskRequestId=x.TaskRequestId, UploaderUserId=x.UploaderUserId, UploaderUserName=x.UploaderUser.UserName,
      Kind=x.Kind.ToString(), TaskMessageId=x.TaskMessageId, TaskSubmissionId=x.TaskSubmissionId, SubTaskSubmissionId=x.SubTaskSubmissionId,
      FileName=x.OriginalFileName, ContentType=x.ContentType, SizeBytes=x.SizeBytes, CreatedAt=x.CreatedAt,
      CanDelete=x.Kind==TaskAttachmentKind.Task && (x.UploaderUserId==userId || ownerId==userId) };
    private static bool Starts(byte[] value, byte[] prefix) => value.AsSpan().StartsWith(prefix);
    private static bool IsJpeg(byte[] b) => b.Length >= 3 && b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff;
    private static bool IsZip(byte[] b) => b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4b &&
        ((b[2] == 3 && b[3] == 4) || (b[2] == 5 && b[3] == 6) || (b[2] == 7 && b[3] == 8));
    private static bool IsText(byte[] b) => !b.Contains((byte)0);
}
