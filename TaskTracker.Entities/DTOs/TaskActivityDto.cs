namespace TaskTracker.Entities.DTOs;

public class TaskActivityDto
{
    public int Id { get; set; }
    public string ActivityType { get; set; } = null!;
    public int ActorUserId { get; set; }
    public string ActorUserName { get; set; } = null!;
    public int? TargetUserId { get; set; }
    public string? TargetUserName { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}
