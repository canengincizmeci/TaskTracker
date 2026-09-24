using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Entities.DTOs;

public enum WorkTaskNextAction
{
    View,
    Start,
    Submit,
    Revise,
    Review
}

public class WorkTaskListItemDto
{
    public int TaskId { get; set; }
    public long Version { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Category { get; set; } = null!;
    public TaskStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public DateOnly? DueDate { get; set; }
    public int OwnerId { get; set; }
    public string OwnerUserName { get; set; } = null!;
    public int? AssigneeUserId { get; set; }
    public string? AssigneeUserName { get; set; }
    public bool IsOwned { get; set; }
    public bool IsAssigned { get; set; }
    public bool IsShared { get; set; }
    public WorkTaskNextAction NextAction { get; set; }
    public int? LatestSubmissionId { get; set; }
    public DateTime? ReviewSubmittedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
