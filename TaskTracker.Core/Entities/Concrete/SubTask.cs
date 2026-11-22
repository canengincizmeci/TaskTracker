using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Core.Entities.Concrete;

public class SubTask : IEntity
{
    public const int MaxTitleLength = TaskRequest.MaxTitleLength;
    public const int MaxDescriptionLength = TaskRequest.MaxDescriptionLength;

    public int Id { get; set; }
    public int TaskRequestId { get; init; }
    public int CreatedByUserId { get; init; }
    public int? AssigneeUserId { get; set; }
    public long Version { get; private set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public TaskStatus Status { get; set; } = TaskStatus.Pending;
    public DateOnly? DueDate { get; set; }
    public bool Activity { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }
    public TaskRequest TaskRequest { get; init; } = null!;
    public User CreatedByUser { get; init; } = null!;
    public User? Assignee { get; set; }
    public User? DeletedByUser { get; set; }
}
