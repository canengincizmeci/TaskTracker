using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete;

/// <summary>Append-only audit history for security-sensitive workspace changes.</summary>
public class WorkspaceActivity : IEntity
{
    public int Id { get; init; }
    public int WorkspaceId { get; init; }
    public int ActorUserId { get; init; }
    public int? TargetUserId { get; init; }
    public WorkspaceActivityType ActivityType { get; init; }
    public WorkspaceRole? FromRole { get; init; }
    public WorkspaceRole? ToRole { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public Workspace Workspace { get; init; } = null!;
    public User ActorUser { get; init; } = null!;
    public User? TargetUser { get; init; }
}
