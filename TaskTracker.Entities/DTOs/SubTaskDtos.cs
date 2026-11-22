using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Entities.DTOs;

public record CreateSubTaskDto
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TaskPriority Priority { get; init; } = TaskPriority.Medium;
    public DateOnly? DueDate { get; init; }
    public int UtcOffsetMinutes { get; init; }
    public int? AssigneeUserId { get; init; }
}

public sealed record UpdateSubTaskDto : CreateSubTaskDto
{
    public long Version { get; init; } = -1;
}

public record SubTaskCommandDto
{
    public long Version { get; init; } = -1;
}

public sealed record AssignSubTaskDto : SubTaskCommandDto
{
    public int AssigneeUserId { get; init; }
}

public sealed record CreateSubTaskSubmissionDto : SubTaskCommandDto
{
    public string Content { get; init; } = string.Empty;
}

public sealed record ReviewSubTaskSubmissionDto : SubTaskCommandDto
{
    public TaskReviewDecision Decision { get; init; }
    public string? Feedback { get; init; }
}

public sealed record SubTaskDto(int Id, int TaskRequestId, int CreatedByUserId, int? AssigneeUserId, long Version,
    string Title, string Description, TaskPriority Priority, TaskStatus Status, DateOnly? DueDate, DateTime CreatedAt,
    int? CurrentSubmissionId)
{
    public string? AssigneeUserName { get; init; }
    public SubTaskCapabilitiesDto? Capabilities { get; init; }
}

public sealed record SubTaskCapabilitiesDto(bool CanEdit, bool CanDelete, bool CanAssign, bool CanStart,
    bool CanComplete, bool CanSubmit, bool CanReview, bool CanCancel, bool CanReopen);

public sealed record AssignedSubTaskDto(int Id, int TaskRequestId, string ParentTitle, TaskStatus ParentStatus,
    string Title, TaskPriority Priority, TaskStatus Status, DateOnly? DueDate);

public sealed record SubTaskSubmissionDto(int Id, int RevisionNumber, int SubmittedByUserId, string Content,
    DateTime CreatedAt, SubTaskReviewDto? Review)
{
    public string? SubmittedByUserName { get; init; }
}

public sealed record SubTaskReviewDto(int Id, int ReviewerUserId, TaskReviewDecision Decision, string? Feedback,
    DateTime CreatedAt)
{
    public string? ReviewerUserName { get; init; }
}

public sealed record SubTaskActivityDto(int Id, int ActorUserId, TaskActivityType ActivityType, DateTime CreatedAt,
    int? TargetUserId, int? SubmissionId, int? ReviewId, TaskStatus? FromStatus, TaskStatus? ToStatus)
{
    public string? ActorUserName { get; init; }
    public string? TargetUserName { get; init; }
}
