namespace TaskTracker.Entities.DTOs;

public class WorkspaceTaskCreateDto : TaskRequestCreateDto
{
    public int? AssigneeUserId { get; set; }
}
