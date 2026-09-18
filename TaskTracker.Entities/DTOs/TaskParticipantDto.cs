namespace TaskTracker.Entities.DTOs;

public class TaskParticipantDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
    public string? Permission { get; set; }
    public DateTime? SharedAt { get; set; }
}
