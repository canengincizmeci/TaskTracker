using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete;

/// <summary>An immutable full snapshot of task planning fields.</summary>
public class TaskPlanningRevision : IEntity
{
    public int Id { get; init; }
    public int TaskRequestId { get; init; }
    public int RevisionNumber { get; init; }
    public int ChangedByUserId { get; init; }
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public TaskPriority Priority { get; init; }
    public DateOnly? DueDate { get; init; }
    public TaskRequest TaskRequest { get; init; } = null!;
    public User ChangedByUser { get; init; } = null!;
}
