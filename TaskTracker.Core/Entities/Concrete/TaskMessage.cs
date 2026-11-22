namespace TaskTracker.Core.Entities.Concrete;

public class TaskMessage : IEntity
{
    public const int MaxContentLength = 4000;
    public const int MaxDeliveryFileCount = 10;
    public int Id { get; init; }
    public int TaskRequestId { get; init; }
    public int SenderUserId { get; init; }
    public string Content { get; init; } = null!;
    public Guid? ClientRequestId { get; init; }
    public string? DeliveryFingerprint { get; init; }
    public int DeliveryFileCount { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public TaskRequest TaskRequest { get; init; } = null!;
    public User SenderUser { get; init; } = null!;
}
