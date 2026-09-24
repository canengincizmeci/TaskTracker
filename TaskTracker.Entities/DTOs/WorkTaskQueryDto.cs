using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Entities.DTOs;

public enum WorkTaskScope
{
    All,
    Owned,
    Assigned,
    Shared
}

public enum WorkTaskDueFilter
{
    All,
    Overdue,
    Today,
    Soon,
    None
}

public enum WorkTaskSort
{
    Due,
    Priority,
    Recent,
    ReviewAge
}

public class WorkTaskQueryDto
{
    public WorkTaskScope Scope { get; set; } = WorkTaskScope.All;
    public string? Search { get; set; }
    public TaskStatus? Status { get; set; }
    public TaskPriority? Priority { get; set; }
    public WorkTaskDueFilter Due { get; set; } = WorkTaskDueFilter.All;
    public WorkTaskSort Sort { get; set; } = WorkTaskSort.Due;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
