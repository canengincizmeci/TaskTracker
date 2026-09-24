namespace TaskTracker.Entities.DTOs;

public class AwaitingReviewTaskDto
{
    public int TaskId { get; set; }
    public string Title { get; set; } = null!;
    public long Version { get; set; }
    public int? AssigneeUserId { get; set; }
    public string? AssigneeUserName { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? LatestSubmissionId { get; set; }
    public int? LatestRevisionNumber { get; set; }
    public DateTime? SubmittedAt { get; set; }
}
