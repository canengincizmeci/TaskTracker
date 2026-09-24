namespace TaskTracker.Entities.DTOs;

public class PagedWorkTasksDto
{
    public List<WorkTaskListItemDto> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}
