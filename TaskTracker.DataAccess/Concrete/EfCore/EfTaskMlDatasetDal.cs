using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.DataAccess.Abstract;

namespace TaskTracker.DataAccess.Concrete.EfCore;

public class EfTaskMlDatasetDal(TaskTrackerDbContext context) : ITaskMlDatasetDal
{
    public const int MaxTasks = 5_000;
    public const int MaxHistoryRecords = 50_000;

    public async Task<List<TaskMlDatasetSourceRecord>?> GetAuthorizedRecordsAsync(int userId, int? workspaceId = null,
        CancellationToken ct = default)
    {
        var query = context.TaskRequests.AsNoTracking();

        query = workspaceId.HasValue
            ? query.Where(x => x.WorkspaceId == workspaceId.Value &&
                x.Workspace!.Members.Any(m => m.UserId == userId && m.IsActive) &&
                (x.Activity || x.OwnerId == userId))
            : query.Where(x =>
                (x.WorkspaceId == null && (x.OwnerId == userId ||
                    (x.Activity && x.TaskShares.Any(s => s.SharedWithUserId == userId &&
                        s.Permission >= TaskPermission.View && s.Permission <= TaskPermission.Manage)))) ||
                (x.WorkspaceId != null &&
                    x.Workspace!.Members.Any(m => m.UserId == userId && m.IsActive) &&
                    (x.Activity || x.OwnerId == userId)));

        var tasks = await query
            .OrderBy(x => x.Id)
            .Take(MaxTasks + 1)
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.Activity,
                x.CreatedAt,
                x.DeletedAt
            })
            .ToListAsync(ct);

        if (tasks.Count == 0)
            return [];
        if (tasks.Count > MaxTasks)
            return null;

        var taskIds = tasks.Select(x => x.Id).ToArray();

        var snapshots = await context.TaskPlanningSnapshots
            .AsNoTracking()
            .Where(x => taskIds.Contains(x.TaskRequestId))
            .ToDictionaryAsync(x => x.TaskRequestId, ct);

        var activities = await context.TaskActivities
            .AsNoTracking()
            .Where(x => taskIds.Contains(x.TaskRequestId) && (x.ToStatus != null ||
                x.ActivityType == TaskActivityType.TaskCreated || x.ActivityType == TaskActivityType.TaskReopened ||
                x.ActivityType == TaskActivityType.TaskCancelled || x.ActivityType == TaskActivityType.TaskDeleted))
            .OrderBy(x => x.TaskRequestId)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Take(MaxHistoryRecords + 1)
            .Select(x => new TaskActivity
            {
                Id = x.Id,
                TaskRequestId = x.TaskRequestId,
                CreatedAt = x.CreatedAt,
                ActivityType = x.ActivityType,
                ToStatus = x.ToStatus
            })
            .ToListAsync(ct);

        if (activities.Count > MaxHistoryRecords)
            return null;

        var revisions = await context.TaskPlanningRevisions
            .AsNoTracking()
            .Where(x => taskIds.Contains(x.TaskRequestId) && x.RevisionNumber == 1)
            .Select(x => new
            {
                x.TaskRequestId,
                x.RevisionNumber,
                x.ChangedAt
            })
            .ToListAsync(ct);

        var activitiesByTask = activities
            .GroupBy(x => x.TaskRequestId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TaskActivity>)g.ToList());

        var revisionsByTask = revisions
            .GroupBy(x => x.TaskRequestId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TaskPlanningRevisionProvenance>)g.Select(r =>
                new TaskPlanningRevisionProvenance
                {
                    RevisionNumber = r.RevisionNumber,
                    ChangedAt = r.ChangedAt
                }).ToList());

        return tasks.Select(task => new TaskMlDatasetSourceRecord
        {
            TaskId = task.Id,
            Status = task.Status,
            Activity = task.Activity,
            CreatedAt = task.CreatedAt,
            DeletedAt = task.DeletedAt,
            Snapshot = snapshots.GetValueOrDefault(task.Id),
            Activities = activitiesByTask.GetValueOrDefault(task.Id) ?? [],
            RevisionProvenance = revisionsByTask.GetValueOrDefault(task.Id) ?? []
        }).ToList();
    }

    public Task<TaskPlanningSnapshot?> GetSnapshotAsync(int taskId, CancellationToken ct = default) =>
        context.TaskPlanningSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TaskRequestId == taskId, ct);
}
