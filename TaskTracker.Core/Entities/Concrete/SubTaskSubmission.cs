namespace TaskTracker.Core.Entities.Concrete;

public class SubTaskSubmission : IEntity
{
    public const int MaxContentLength = TaskSubmission.MaxContentLength;
    public const int MaxDeliveryFileCount = 10;

    public int Id { get; init; }
    public int SubTaskId { get; init; }
    // The subtask's composite foreign key also fixes the authorization scope of its evidence.
    public int TaskRequestId { get; init; }
    public int SubmittedByUserId { get; init; }
    public int RevisionNumber { get; init; }
    public required string Content { get; init; }
    public Guid? ClientRequestId { get; init; }
    public string? DeliveryFingerprint { get; init; }
    public int DeliveryFileCount { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public SubTask SubTask { get; init; } = null!;
    public User SubmittedByUser { get; init; } = null!;
}
