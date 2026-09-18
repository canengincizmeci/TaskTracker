namespace TaskTracker.Entities.DTOs;

public class TaskMessageDto
{
    public int Id { get; set; }
    public int SenderUserId { get; set; }
    public string SenderUserName { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public class CreateTaskMessageDto
{
    public string? Content { get; set; }
}
