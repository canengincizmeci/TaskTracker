namespace TaskTracker.Entities.DTOs;

public class TaskSubmissionReviewDto
{
    public int Id { get; set; }
    public int ReviewerUserId { get; set; }
    public string ReviewerUserName { get; set; } = null!;
    public string Decision { get; set; } = null!;
    public string? Feedback { get; set; }
    public DateTime CreatedAt { get; set; }
}
