using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Core.Entities.Concrete;

/// <summary>Structured, append-only history; display text is derived by readers.</summary>
public class TaskActivity : IEntity
{
    public int Id { get; init; }
    public int TaskRequestId { get; init; }
    public int ActorUserId { get; init; }
    public TaskActivityType ActivityType { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public int? TargetUserId { get; init; }
    public int? InvitationId { get; init; }
    public int? SubmissionId { get; init; }
    public int? ReviewId { get; init; }
    public TaskStatus? FromStatus { get; init; }
    public TaskStatus? ToStatus { get; init; }
    public TaskRequest TaskRequest { get; init; } = null!;
    public User ActorUser { get; init; } = null!;
    public User? TargetUser { get; init; }
    public TaskShareInvitation? Invitation { get; init; }
    public TaskSubmission? Submission { get; init; }
    public TaskSubmissionReview? Review { get; init; }
}
