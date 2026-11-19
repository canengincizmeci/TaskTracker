using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete;

public sealed class TaskAttachment : IEntity
{
    public int Id { get; set; }
    public int TaskRequestId { get; set; }
    public int UploaderUserId { get; set; }
    public TaskAttachmentKind Kind { get; set; }
    public int? TaskMessageId { get; set; }
    public int? TaskSubmissionId { get; set; }
    public required string OriginalFileName { get; set; }
    public required string StorageKey { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
    public TaskRequest TaskRequest { get; set; } = null!;
    public User UploaderUser { get; set; } = null!;
    public TaskMessage? TaskMessage { get; set; }
    public TaskSubmission? TaskSubmission { get; set; }
}
