using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.API.Controllers;

[ApiController, Route("api/[controller]"), Authorize(Roles = "User")]
public sealed class TaskAttachmentController(
    TaskTrackerDbContext db, ICurrentUserService currentUser, ITaskCollaborationService collaboration,
    ITaskFileStore store, ITaskFileScanner scanner, IOptions<TaskFileOptions> options) : ControllerBase
{
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
    public async Task<IActionResult> List(int taskId)
    {
        var userId = currentUser.UserId;
        if (!await collaboration.CanAccessAsync(taskId, userId)) return Forbid();
        var ownerId = await db.TaskRequests.Where(x => x.Id == taskId).Select(x => x.OwnerId).SingleAsync();
        var entities = await db.TaskAttachments.AsNoTracking().Where(x => x.TaskRequestId == taskId && !x.IsDeleted)
            .Include(x => x.UploaderUser).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .ToListAsync();
        return Ok(entities.Select(x => Map(x, userId, ownerId)));
    }

    [HttpPost("task/{taskId:int}"), RequestSizeLimit(21 * 1024 * 1024)]
    public Task<IActionResult> UploadTask(int taskId, IFormFile file, CancellationToken ct) =>
        Upload(taskId, file, TaskAttachmentKind.Task, null, null, ct);

    [HttpPost("task/{taskId:int}/messages/{messageId:int}"), RequestSizeLimit(21 * 1024 * 1024)]
    public Task<IActionResult> UploadMessage(int taskId, int messageId, IFormFile file, CancellationToken ct) =>
        Upload(taskId, file, TaskAttachmentKind.Message, messageId, null, ct);

    [HttpPost("task/{taskId:int}/submissions/{submissionId:int}"), RequestSizeLimit(21 * 1024 * 1024)]
    public Task<IActionResult> UploadSubmission(int taskId, int submissionId, IFormFile file, CancellationToken ct) =>
        Upload(taskId, file, TaskAttachmentKind.Submission, null, submissionId, ct);

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var userId = currentUser.UserId;
        var attachment = await db.TaskAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (attachment is null) return NotFound();
        if (!await collaboration.CanAccessAsync(attachment.TaskRequestId, userId)) return Forbid();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox";
        return File(store.OpenRead(attachment.StorageKey), "application/octet-stream", attachment.OriginalFileName,
            enableRangeProcessing: true);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = currentUser.UserId;
        var attachment = await db.TaskAttachments.Include(x => x.TaskRequest)
            .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
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

    private async Task<IActionResult> Upload(int taskId, IFormFile file, TaskAttachmentKind kind,
        int? messageId, int? submissionId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (!await collaboration.CanAccessAsync(taskId, userId)) return Forbid();
        if (file is null || file.Length <= 0) return BadRequest("Choose a non-empty file.");
        if (file.Length > options.Value.MaxFileSizeBytes) return StatusCode(413, "The file exceeds the configured size limit.");
        var name = Path.GetFileName(file.FileName).Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return BadRequest("The filename is invalid or too long.");
        var extension = Path.GetExtension(name);
        if (!Allowed.TryGetValue(extension, out var rule)) return BadRequest("This file type is not allowed.");
        await using var source = file.OpenReadStream();
        var header = new byte[Math.Min(512, checked((int)file.Length))];
        var read = await source.ReadAsync(header, ct);
        Array.Resize(ref header, read);
        if (!rule.Signature(header)) return BadRequest("The file content does not match its extension.");
        source.Position = 0;
        var verdict = await scanner.ScanAsync(source, name, ct);
        if (verdict == TaskFileScanVerdict.Rejected)
            return BadRequest("The file was rejected by the configured security scanner.");
        if (options.Value.RequireCleanScan && verdict != TaskFileScanVerdict.Clean)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "File scanning is unavailable. Try again later.");
        source.Position = 0;
        if (!await collaboration.CanAccessAsync(taskId, userId)) return Forbid();
        var task = await db.TaskRequests.SingleOrDefaultAsync(x => x.Id == taskId && x.Activity, ct);
        if (task is null) return Forbid();
        if (kind == TaskAttachmentKind.Message && !await db.TaskMessages.AnyAsync(x => x.Id == messageId &&
                x.TaskRequestId == taskId && x.SenderUserId == userId, ct))
            return BadRequest("Files can only be attached to your own message in this task.");
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
            TaskMessageId = messageId, TaskSubmissionId = submissionId, OriginalFileName = name,
            StorageKey = key, ContentType = rule.Mime, SizeBytes = file.Length, CreatedAt = DateTime.UtcNow };
        try
        {
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

    private static TaskAttachmentDto Map(TaskAttachment x, int userId, int ownerId) => new()
    { Id=x.Id, TaskRequestId=x.TaskRequestId, UploaderUserId=x.UploaderUserId, UploaderUserName=x.UploaderUser.UserName,
      Kind=x.Kind.ToString(), TaskMessageId=x.TaskMessageId, TaskSubmissionId=x.TaskSubmissionId,
      FileName=x.OriginalFileName, ContentType=x.ContentType, SizeBytes=x.SizeBytes, CreatedAt=x.CreatedAt,
      CanDelete=x.Kind==TaskAttachmentKind.Task && (x.UploaderUserId==userId || ownerId==userId) };
    private static bool Starts(byte[] value, byte[] prefix) => value.AsSpan().StartsWith(prefix);
    private static bool IsJpeg(byte[] b) => b.Length >= 3 && b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff;
    private static bool IsZip(byte[] b) => b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4b &&
        ((b[2] == 3 && b[3] == 4) || (b[2] == 5 && b[3] == 6) || (b[2] == 7 && b[3] == 8));
    private static bool IsText(byte[] b) => !b.Contains((byte)0);
}
