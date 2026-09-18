using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Bussiness.Abstract;

public interface ITaskActivityWriter
{
    Task<TaskActivity> WriteAsync(TaskRequest task, int actorUserId, TaskActivityType type,
        int? targetUserId = null, TaskShareInvitation? invitation = null);
}
