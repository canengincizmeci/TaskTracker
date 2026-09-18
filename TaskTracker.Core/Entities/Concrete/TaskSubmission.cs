namespace TaskTracker.Core.Entities.Concrete;

/// <summary>An immutable revision. Corrections are represented by another row.</summary>
public class TaskSubmission : IEntity
{
    public const int MaxContentLength = 10000;

    public int Id { get; init; }
    public int TaskRequestId { get; init; }
    public int SubmittedByUserId { get; init; }
    public int RevisionNumber { get; init; }
    public required string Content { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public TaskRequest TaskRequest { get; init; } = null!;
    public User SubmittedByUser { get; init; } = null!;
}
