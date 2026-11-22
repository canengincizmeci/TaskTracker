using TaskTracker.Core.Entities.Concrete;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.DataAccess.Abstract;

public sealed class TaskPlanningRevisionProvenance
{
    public required int RevisionNumber { get; init; }
    public required DateTime ChangedAt { get; init; }
}

public sealed class TaskMlDatasetSourceRecord
{
    public required int TaskId { get; init; }
    public required TaskStatus Status { get; init; }
    public required bool Activity { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
    public TaskPlanningSnapshot? Snapshot { get; init; }
    public required IReadOnlyList<TaskActivity> Activities { get; init; }
    public required IReadOnlyList<TaskPlanningRevisionProvenance> RevisionProvenance { get; init; }
}

public interface ITaskMlDatasetDal
{
    Task<List<TaskMlDatasetSourceRecord>> GetAuthorizedRecordsAsync(int userId, int? workspaceId = null,
        CancellationToken ct = default);
    Task<TaskPlanningSnapshot?> GetSnapshotAsync(int taskId, CancellationToken ct = default);
}
