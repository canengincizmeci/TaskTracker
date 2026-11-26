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
    ILogger<TaskAttachmentController>? logger = null, INotificationService? notifications = null) : ControllerBase
{
    private const long LegacyUploadRequestLimit = 21 * 1024 * 1024;
    private bool ScannerConfigured => scanner is not NoOpTaskFileScanner &&
        (scanner is not ClamAvTaskFileScanner clamAv || clamAv.IsConfigured);
    private bool SubTaskUploadsEnabled => options.Value.EnableSubTaskSubmissionUploads &&
        ScannerConfigured && SubTaskAttachmentLimit > 0 && SubTaskFileLimit > 0;
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

    [HttpPost("task/{taskId:int}"), RequestSizeLimit(LegacyUploadRequestLimit)]
    [ServiceFilter(typeof(TaskUploadFilter))]
    [RequestFormLimits(ValueCountLimit = 12)]
    [Authorize(Roles = "User")]
    public Task<IActionResult> UploadTask(int taskId, IFormFile file, CancellationToken ct) =>
        Upload(taskId, file, TaskAttachmentKind.Task, null, null, ct);

    [HttpPost("task/{taskId:int}/messages/{messageId:int}"), RequestSizeLimit(LegacyUploadRequestLimit)]
    [ServiceFilter(typeof(TaskUploadFilter))]
    [RequestFormLimits(ValueCountLimit = 12)]
    [Authorize(Roles = "User")]
    public Task<IActionResult> UploadMessage(int taskId, int messageId, IFormFile file, CancellationToken ct) =>
        Upload(taskId, file, TaskAttachmentKind.Message, messageId, null, ct);

    [HttpPost("task/{taskId:int}/submissions/{submissionId:int}"), RequestSizeLimit(LegacyUploadRequestLimit)]
    [ServiceFilter(typeof(TaskUploadFilter))]
    [RequestFormLimits(ValueCountLimit = 12)]
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
            files.Select(x => Map(x, userId, ownerId: 0)).ToList())
        {
            MaxFiles = SubTaskAttachmentLimit,
            MaxFileSizeBytes = Math.Max(0, SubTaskFileLimit),
            MaxTotalBytes = SubTaskTotalLimit
        });
    }

    [HttpPost("/api/TaskRequest/{taskId:int}/subtasks/{subTaskId:int}/submissions/{submissionId:int}/files")]
    [RequestSizeLimit(LegacyUploadRequestLimit)]
    [ServiceFilter(typeof(TaskUploadFilter))]
    [RequestFormLimits(ValueCountLimit = 12)]
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
        var ct = HttpContext?.RequestAborted ?? CancellationToken.None;
        var taskId = await ParentAttachments().Where(x => x.Id == id).Select(x => (int?)x.TaskRequestId).SingleOrDefaultAsync(ct);
        if (taskId is null) return NotFound();
        TaskAttachment attachment;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await LockSubTaskScopeAsync(taskId.Value, ct, exclusiveParent: true);
            if (!await collaboration.CanAccessAsync(taskId.Value, userId, ct)) return Forbid();
            var current = await ParentAttachments().Include(x => x.TaskRequest)
                .SingleOrDefaultAsync(x => x.Id == id && x.TaskRequestId == taskId.Value, ct);
            if (current is null) return NotFound();
            if (current.Kind != TaskAttachmentKind.Task) return Conflict("Discussion and submission evidence is immutable.");
            if (current.UploaderUserId != userId && current.TaskRequest.OwnerId != userId) return Forbid();
            attachment = current;
            attachment.IsDeleted = true;
            attachment.DeletedAt = DateTime.UtcNow;
            attachment.DeletedByUserId = userId;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(CancellationToken.None);
        }
        RemoveStagedFile(attachment.StorageKey);
        await collaboration.PublishAttachmentsChangedAsync(taskId.Value);
        return NoContent();
    }

    private IQueryable<TaskAttachment> ParentAttachments() => db.TaskAttachments
        .Where(x => !x.IsDeleted && x.Kind != TaskAttachmentKind.SubTaskSubmission);

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

    private IQueryable<TaskAttachment> SubTaskFiles(int taskId, int subTaskId) => db.TaskAttachments.AsNoTracking()
        .Where(x => x.TaskRequestId == taskId && x.Kind == TaskAttachmentKind.SubTaskSubmission && !x.IsDeleted &&
            x.SubTaskSubmission != null && x.SubTaskSubmission.SubTaskId == subTaskId && x.SubTaskSubmission.SubTask.Activity);

    private Task<bool> CanUploadSubTaskFileAsync(int taskId, int subTaskId, int submissionId, int userId, CancellationToken ct) =>
        db.SubTaskSubmissions.AnyAsync(x => x.Id == submissionId && x.TaskRequestId == taskId && x.SubTaskId == subTaskId &&
            x.SubmittedByUserId == userId && x.ClientRequestId == null && x.SubTask.Activity && x.SubTask.Status == TaskStatus.InReview &&
            x.SubTask.AssigneeUserId == userId && x.SubTask.TaskRequest.OwnerId != userId &&
            (x.SubTask.TaskRequest.Status == TaskStatus.Pending || x.SubTask.TaskRequest.Status == TaskStatus.InProgress) &&
            (x.SubTask.TaskRequest.WorkspaceId != null ? x.SubTask.TaskRequest.Workspace!.Members.Any(m => m.IsActive && m.UserId == userId)
                : x.SubTask.TaskRequest.TaskShares.Any(s => s.SharedWithUserId == userId &&
                    s.Permission >= TaskPermission.Edit && s.Permission <= TaskPermission.Manage)) &&
            !db.SubTaskSubmissionReviews.Any(r => r.SubTaskSubmissionId == x.Id) &&
            !db.SubTaskSubmissions.Any(s => s.SubTaskId == subTaskId && s.RevisionNumber > x.RevisionNumber), ct);

    private async Task LockSubTaskScopeAsync(int taskId, CancellationToken ct = default, bool exclusiveParent = false)
    {
        await subTasks.LockParentWorkspaceAsync(taskId, ct);
        await subTasks.LockParentAsync(taskId, exclusiveParent, ct);
    }

    private async Task CleanupUploadAsync(int taskId, IEnumerable<string> staged, bool commitStarted)
    {
        var keys = staged.ToArray();
        if (keys.Length == 0) return;
        if (commitStarted)
        {
            // A lost commit acknowledgement can leave durable references. Fence the former
            // writer with the parent lock before deciding whether its files can be removed.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var ct = cleanup.Token;
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await LockSubTaskScopeAsync(taskId, ct, exclusiveParent: true);
                var saved = await db.TaskAttachments.AsNoTracking().Where(x => keys.Contains(x.StorageKey))
                    .Select(x => x.StorageKey).ToListAsync(ct);
                keys = keys.Except(saved).ToArray();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not verify the commit outcome for uploaded files on task {TaskId}", taskId);
                return;
            }
        }

        foreach (var key in keys) RemoveStagedFile(key);
    }

    private void RemoveStagedFile(string key)
    {
        try { store.Delete(key); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Could not remove a private task file");
        }
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
