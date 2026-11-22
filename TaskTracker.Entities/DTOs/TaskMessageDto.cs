using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Entities.DTOs;

public class TaskMessageDto
{
    public int Id { get; set; }
    public int SenderUserId { get; set; }
    public string SenderUserName { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public int DeliveryFileCount { get; set; }

    public static TaskMessageDto From(TaskMessage message) => new()
    {
        Id = message.Id, SenderUserId = message.SenderUserId, SenderUserName = message.SenderUser.UserName,
        Content = message.Content, CreatedAt = message.CreatedAt, DeliveryFileCount = message.DeliveryFileCount
    };
}

public class CreateTaskMessageDto
{
    public string? Content { get; set; }
}
