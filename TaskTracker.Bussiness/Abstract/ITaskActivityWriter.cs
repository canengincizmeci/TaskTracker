using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Bussiness.Abstract;

public interface ITaskActivityWriter
{
    Task<TaskActivity> WriteAsync(TaskRequest task, int actorUserId, TaskActivityType type,
        int? targetUserId = null, TaskShareInvitation? invitation = null,
        TaskStatus? fromStatus = null, TaskStatus? toStatus = null);
}
