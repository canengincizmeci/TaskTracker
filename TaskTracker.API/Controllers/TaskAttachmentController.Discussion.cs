using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.API.Services;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.API.Controllers;

public sealed partial class TaskAttachmentController
{
    private const long DiscussionBytesCeiling = 100L * 1024 * 1024;
    private const long DiscussionRequestLimit = DiscussionBytesCeiling + 1024 * 1024;
    private int DiscussionAttachmentLimit => Math.Clamp(options.Value.MaxDiscussionAttachments, 0, TaskMessage.MaxDeliveryFileCount);
    private long DiscussionTotalLimit => Math.Clamp(options.Value.MaxDiscussionUploadBytes, 0, DiscussionBytesCeiling);
    private long DiscussionFileLimit => Math.Min(options.Value.MaxFileSizeBytes, DiscussionTotalLimit);
    private bool DiscussionUploadsEnabled => User.IsInRole("User") &&
        options.Value.EnableDiscussionUploads && ScannerConfigured &&
        DiscussionAttachmentLimit > 0 && DiscussionFileLimit > 0;

    public sealed class DiscussionDeliveryForm
    {
        public Guid ClientRequestId { get; set; }
        public string? Content { get; set; }
        public List<IFormFile> Files { get; set; } = [];
    }

    [HttpGet("/api/TaskWorkspace/{taskId:int}/messages/attachment-settings")]
    public async Task<IActionResult> DiscussionAttachmentSettings(int taskId, CancellationToken ct)
    {
        if (!await collaboration.CanAccessAsync(taskId, currentUser.UserId, ct)) return Forbid();
        return Ok(new
        {
            uploadsEnabled = DiscussionUploadsEnabled,
            maxFiles = DiscussionAttachmentLimit,
            maxFileSizeBytes = Math.Max(0, DiscussionFileLimit),
            maxTotalBytes = DiscussionTotalLimit
        });
    }

    [HttpPost("/api/TaskWorkspace/{taskId:int}/messages/delivery")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(DiscussionRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = DiscussionRequestLimit, ValueCountLimit = 16)]
    [ServiceFilter(typeof(TaskUploadFilter))]
    public async Task<IActionResult> DeliverDiscussionMessage(int taskId, [FromForm] DiscussionDeliveryForm form, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (!await collaboration.CanAccessAsync(taskId, userId, ct)) return Forbid();
        var content = form.Content?.Trim() ?? "";
        if (form.ClientRequestId == Guid.Empty) return BadRequest("A delivery identifier is required.");
        if ((form.Content?.Length ?? 0) > TaskMessage.MaxContentLength)
            return BadRequest("Message text must contain at most 4000 characters.");
        if (content.Length == 0 && form.Files.Count == 0) return BadRequest("Write a message or attach at least one file.");
        if (form.Files.Count > TaskMessage.MaxDeliveryFileCount)
            return BadRequest($"Send at most {TaskMessage.MaxDeliveryFileCount} files per message.");

        var uploads = new List<UploadFile>(form.Files.Count);
        foreach (var file in form.Files)
        {
            var (upload, failure) = InspectUpload(file, DiscussionBytesCeiling);
            if (failure is not null) return failure;
            uploads.Add(upload!);
        }
        var totalBytes = uploads.Sum(x => x.File.Length);
        if (totalBytes > DiscussionBytesCeiling) return StatusCode(413, "The files exceed the maximum message size.");

        var identities = new List<DiscussionFileIdentity>(uploads.Count);
        foreach (var upload in uploads)
        {
            await using var source = upload.File.OpenReadStream();
            identities.Add(new(upload.Name, upload.File.Length, Convert.ToHexString(await SHA256.HashDataAsync(source, ct))));
        }
        if (identities.Distinct().Count() != uploads.Count)
            return BadRequest("Remove duplicate files before sending the message.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { content, identities })));

        // An acknowledged delivery can be replayed even if uploads have since been disabled.
        await using (var read = await db.Database.BeginTransactionAsync(ct))
        {
            await LockSubTaskScopeAsync(taskId, ct);
            if (!await collaboration.CanAccessAsync(taskId, userId, ct)) return Forbid();
            var previous = await FindDiscussionDeliveryAsync(taskId, userId, form.ClientRequestId, ct);
            if (previous is not null) return DiscussionReceipt(previous, fingerprint);
            await read.CommitAsync(ct);
        }

        if (uploads.Count > 0)
        {
            if (!DiscussionUploadsEnabled) return DiscussionUnavailable();
            var quotaFailure = CheckDiscussionQuota(uploads.Count, totalBytes);
            if (quotaFailure is not null) return quotaFailure;
            var oversized = uploads.FirstOrDefault(x => x.File.Length > DiscussionFileLimit);
            if (oversized is not null) return StatusCode(413, $"'{oversized.Name}' exceeds the configured file size limit.");
        }

        var staged = new List<(UploadFile File, string Key)>(uploads.Count);
        var commitStarted = false;
        var committed = false;
        TaskMessage message;
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
            await LockSubTaskScopeAsync(taskId, ct, exclusiveParent: true);
            if (!await collaboration.CanAccessAsync(taskId, userId, ct)) return Forbid();
            var previous = await FindDiscussionDeliveryAsync(taskId, userId, form.ClientRequestId, ct);
            if (previous is not null) return DiscussionReceipt(previous, fingerprint);

            var sender = await db.Users.SingleAsync(x => x.Id == userId, ct);
            message = new TaskMessage
            {
                TaskRequestId = taskId, SenderUserId = userId, SenderUser = sender, Content = content,
                ClientRequestId = form.ClientRequestId, DeliveryFingerprint = fingerprint, DeliveryFileCount = staged.Count
            };
            db.TaskMessages.Add(message);
            foreach (var (upload, key) in staged)
            {
                var attachment = DiscussionAttachment(taskId, sender, upload, key);
                attachment.TaskMessage = message;
                db.TaskAttachments.Add(attachment);
            }
            await db.SaveChangesAsync(ct);
            commitStarted = true;
            await transaction.CommitAsync(CancellationToken.None);
            committed = true;
        }
        finally
        {
            if (!committed) await CleanupUploadAsync(taskId, staged.Select(file => file.Key), commitStarted);
        }

        var dto = TaskMessageDto.From(message);
        await collaboration.PublishMessageAsync(taskId, dto);
        if (staged.Count != 0) await collaboration.PublishAttachmentsChangedAsync(taskId);
        return Ok(dto);
    }

    private sealed record DiscussionFileIdentity(string Name, long SizeBytes, string Digest);

    private Task<TaskMessage?> FindDiscussionDeliveryAsync(int taskId, int userId, Guid requestId, CancellationToken ct) =>
        db.TaskMessages.AsNoTracking().Include(x => x.SenderUser).SingleOrDefaultAsync(x =>
            x.TaskRequestId == taskId && x.SenderUserId == userId && x.ClientRequestId == requestId, ct);

    private IActionResult DiscussionReceipt(TaskMessage previous, string fingerprint) =>
        previous.DeliveryFingerprint == fingerprint ? Ok(TaskMessageDto.From(previous)) :
            Conflict(new { code = "delivery_conflict", message = "This delivery identifier was already used for a different message." });

    private IActionResult DiscussionUnavailable() => StatusCode(503,
        new { code = "discussion_upload_unavailable", message = "Discussion file uploads are unavailable." });

    private Task<bool> CanAddLegacyDiscussionFileAsync(int taskId, int messageId, int userId, CancellationToken ct) =>
        db.TaskMessages.AnyAsync(x => x.Id == messageId && x.TaskRequestId == taskId &&
            x.SenderUserId == userId && x.ClientRequestId == null, ct);

    private static TaskAttachment DiscussionAttachment(int taskId, User sender, UploadFile upload, string key) => new()
    {
        TaskRequestId = taskId, UploaderUserId = sender.Id, UploaderUser = sender, Kind = TaskAttachmentKind.Message,
        OriginalFileName = upload.Name, ContentType = upload.Mime, SizeBytes = upload.File.Length, StorageKey = key
    };

    private IActionResult? CheckDiscussionQuota(int count, long bytes)
    {
        if (count > DiscussionAttachmentLimit) return BadRequest($"Send at most {DiscussionAttachmentLimit} files per message.");
        if (bytes > DiscussionTotalLimit) return StatusCode(413, "The files exceed the configured message size limit.");
        return null;
    }
}
