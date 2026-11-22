namespace TaskTracker.Entities.DTOs;

public sealed class TaskAttachmentDto
{
    public int Id { get; set; }
    public int TaskRequestId { get; set; }
    public int UploaderUserId { get; set; }
    public string UploaderUserName { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public int? TaskMessageId { get; set; }
    public int? TaskSubmissionId { get; set; }
    public int? SubTaskSubmissionId { get; set; }
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool CanDelete { get; set; }
}

public sealed record SubTaskFilesDto(long Version, bool UploadsEnabled, int? UploadSubmissionId,
    List<TaskAttachmentDto> Files);
