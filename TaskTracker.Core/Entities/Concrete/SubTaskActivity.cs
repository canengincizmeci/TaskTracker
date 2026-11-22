using System.Collections.Immutable;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Core.Entities.Concrete;

public class SubTaskActivity : IEntity
{
    internal static readonly ImmutableArray<TaskActivityType> SupportedTypes =
    [
        TaskActivityType.TaskCreated, TaskActivityType.TaskDetailsUpdated, TaskActivityType.TaskDeleted,
        TaskActivityType.UserAssigned, TaskActivityType.UserUnassigned, TaskActivityType.WorkStarted,
        TaskActivityType.SubmissionCreated, TaskActivityType.ChangesRequested, TaskActivityType.SubmissionApproved,
        TaskActivityType.TaskCompleted, TaskActivityType.TaskCancelled, TaskActivityType.TaskReopened,
        TaskActivityType.ResponsibilityReset
    ];

    public int Id { get; init; }
    public int TaskRequestId { get; init; }
    public int SubTaskId { get; init; }
    public int ActorUserId { get; init; }
    public TaskActivityType ActivityType { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public int? TargetUserId { get; init; }
    public int? SubmissionId { get; init; }
    public int? ReviewId { get; init; }
    public TaskStatus? FromStatus { get; init; }
    public TaskStatus? ToStatus { get; init; }
    public TaskRequest TaskRequest { get; init; } = null!;
    public SubTask SubTask { get; init; } = null!;
    public User ActorUser { get; init; } = null!;
    public User? TargetUser { get; init; }
    public SubTaskSubmission? Submission { get; init; }
    public SubTaskSubmissionReview? Review { get; init; }
}
