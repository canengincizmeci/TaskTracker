using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete;

public class SubTaskSubmissionReview : IEntity
{
    public const int MaxFeedbackLength = TaskSubmissionReview.MaxFeedbackLength;

    public int Id { get; init; }
    public int SubTaskSubmissionId { get; init; }
    public int ReviewerUserId { get; init; }
    public TaskReviewDecision Decision { get; init; }
    public string? Feedback { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public SubTaskSubmission SubTaskSubmission { get; init; } = null!;
    public User ReviewerUser { get; init; } = null!;
}
