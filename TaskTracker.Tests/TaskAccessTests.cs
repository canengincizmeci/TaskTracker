using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class TaskAccessTests
{
    [Theory]
    [InlineData(1, true, true)]
    [InlineData(2, true, false)]
    [InlineData(3, false, false)]
    public async Task Private_task_access_uses_ownership_or_accepted_share(int userId, bool allowed, bool isOwner)
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShares.Add(new TaskShare { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.View });
        context.TaskShareInvitations.Add(new TaskShareInvitation
        {
            TaskRequestId = 1, InvitedByUserId = 1, InvitedUserId = 3,
            Permission = TaskPermission.Edit, Status = TaskShareInvitationStatus.Pending
        });
        await context.SaveChangesAsync();
        using var unitOfWork = new UnitOfWork(context);
        var manager = new TaskRequestManager(unitOfWork, new EfTaskShareDal(context), new EfTaskRequestDal(context));

        var result = await manager.GetTaskById(1, userId);

        Assert.Equal(allowed, result.Success);
        if (allowed)
        {
            Assert.Equal(isOwner, result.Data.IsOwner);
            Assert.True(result.Data.CanView);
            Assert.Equal(isOwner, result.Data.CanEdit);
        }
        var tasks = await manager.GetTasksByUserId(userId);
        Assert.Equal(allowed ? 1 : 0, tasks.Data.Count);
    }

    [Fact]
    public async Task Default_creation_is_pending_unassigned_and_owned_by_current_user()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        using var unitOfWork = new UnitOfWork(context);
        var manager = new TaskRequestManager(unitOfWork, new EfTaskShareDal(context), new EfTaskRequestDal(context));
        var result = await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "New task", Description = "Default lifecycle", Category = "Tests"
        }, 1);

        Assert.True(result.Success);
        var task = Assert.Single(context.TaskRequests);
        Assert.Equal(1, task.OwnerId);
        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Null(task.AssigneeUserId);
        Assert.Equal(0, task.Version);
    }
}
