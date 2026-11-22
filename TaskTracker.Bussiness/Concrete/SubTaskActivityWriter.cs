using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Bussiness.Concrete;

internal static class SubTaskActivityWriter
{
    internal static Task WriteAsync(IUnitOfWork unitOfWork, SubTask subTask, int actorId, TaskActivityType type,
        int? targetUserId = null, TaskStatus? from = null, TaskStatus? to = null,
        SubTaskSubmission? submission = null, SubTaskSubmissionReview? review = null) =>
        unitOfWork.GetRepository<SubTaskActivity>().AddAsync(new SubTaskActivity
        {
            SubTask = subTask, TaskRequestId = subTask.TaskRequestId, ActorUserId = actorId,
            ActivityType = type, TargetUserId = targetUserId, FromStatus = from, ToStatus = to,
            Submission = submission, Review = review
        });

    internal static async Task SetAssigneeAsync(IUnitOfWork unitOfWork, SubTask subTask, int actorId,
        int? assigneeId)
    {
        if (subTask.AssigneeUserId == assigneeId) return;
        var previous = subTask.AssigneeUserId;
        subTask.AssigneeUserId = assigneeId;
        if (previous.HasValue)
            await WriteAsync(unitOfWork, subTask, actorId, TaskActivityType.UserUnassigned, previous);
        if (subTask.Status == TaskStatus.InProgress)
        {
            subTask.Status = TaskStatus.Pending;
            await WriteAsync(unitOfWork, subTask, actorId, TaskActivityType.ResponsibilityReset,
                from: TaskStatus.InProgress, to: TaskStatus.Pending);
        }
        if (assigneeId.HasValue)
            await WriteAsync(unitOfWork, subTask, actorId, TaskActivityType.UserAssigned, assigneeId);
    }
}
