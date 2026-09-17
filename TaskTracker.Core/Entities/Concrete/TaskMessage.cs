namespace TaskTracker.Core.Entities.Concrete;

public class TaskMessage : IEntity
{
    public const int MaxContentLength = 4000;
    public int Id { get; init; }
    public int TaskRequestId { get; init; }
    public int SenderUserId { get; init; }
    public string Content { get; init; } = null!;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public TaskRequest TaskRequest { get; init; } = null!;
    public User SenderUser { get; init; } = null!;
}
