namespace TaskTracker.Entities.DTOs;

public class TaskSubmissionDto
{
    public int Id { get; set; }
    public int RevisionNumber { get; set; }
    public int SubmittedByUserId { get; set; }
    public string SubmittedByUserName { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public TaskSubmissionReviewDto? Review { get; set; }
}
