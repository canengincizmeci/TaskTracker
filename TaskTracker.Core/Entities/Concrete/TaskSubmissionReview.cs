using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete;

/// <summary>The single immutable final decision for a submission.</summary>
public class TaskSubmissionReview : IEntity
{
    public const int MaxFeedbackLength = 5000;

    public int Id { get; init; }
    public int TaskSubmissionId { get; init; }
    public int ReviewerUserId { get; init; }
    public TaskReviewDecision Decision { get; init; }
    public string? Feedback { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public TaskSubmission TaskSubmission { get; init; } = null!;
    public User ReviewerUser { get; init; } = null!;
}
