using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Bussiness.Concrete;

public class TaskActivityWriter : ITaskActivityWriter
{
    private readonly IUnitOfWork _unitOfWork;

    public TaskActivityWriter(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TaskActivity> WriteAsync(TaskRequest task, int actorUserId, TaskActivityType type,
        int? targetUserId = null, TaskShareInvitation? invitation = null)
    {
        var activity = new TaskActivity
        {
            TaskRequest = task, ActorUserId = actorUserId, ActivityType = type,
            TargetUserId = targetUserId, Invitation = invitation, CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.GetRepository<TaskActivity>().AddAsync(activity);
        return activity;
    }
}
