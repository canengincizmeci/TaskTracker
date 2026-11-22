namespace TaskTracker.Core.Entities.Concrete;

public class SubTaskSubmission : IEntity
{
    public const int MaxContentLength = TaskSubmission.MaxContentLength;

    public int Id { get; init; }
    public int SubTaskId { get; init; }
    // The subtask's composite foreign key also fixes the authorization scope of its evidence.
    public int TaskRequestId { get; init; }
    public int SubmittedByUserId { get; init; }
    public int RevisionNumber { get; init; }
    public required string Content { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public SubTask SubTask { get; init; } = null!;
    public User SubmittedByUser { get; init; } = null!;
}
