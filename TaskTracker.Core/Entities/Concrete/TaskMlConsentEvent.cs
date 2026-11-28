namespace TaskTracker.Core.Entities.Concrete;

public class TaskMlConsentEvent : IEntity
{
    public const string CurrentPolicyVersion = "1.0";
    public const int MaxPolicyVersionLength = 32;

    public int Id { get; init; }
    public int TaskRequestId { get; init; }
    public TaskRequest TaskRequest { get; init; } = null!;
    public int ActorUserId { get; init; }
    public bool Granted { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public required string PolicyVersion { get; init; }
}
