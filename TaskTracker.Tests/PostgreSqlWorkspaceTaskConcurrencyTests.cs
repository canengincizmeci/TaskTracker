using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class PostgreSqlWorkspaceTaskConcurrencyTests
{
    [PostgreSqlTheory]
    [InlineData("assign")]
    [InlineData("reopen")]
    public async Task Member_removal_and_new_active_responsibility_have_one_atomic_winner(string operation)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var setup = database.CreateContext())
        {
            var workspace = new Workspace
            {
                Name = "Concurrent workspace",
                Members =
                [
                    new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner },
                    new WorkspaceMember { UserId = 3, Role = WorkspaceRole.Member }
                ]
            };
            setup.Workspaces.Add(workspace);
            await setup.SaveChangesAsync();
            var seededTask = TestDatabase.Task();
            seededTask.WorkspaceId = workspace.Id;
            seededTask.Status = operation == "reopen" ? TaskStatus.Completed : TaskStatus.Pending;
            seededTask.AssigneeUserId = operation == "reopen" ? 3 : null;
            setup.TaskRequests.Add(seededTask);
            await setup.SaveChangesAsync();
        }

        var barrier = new SaveBarrierInterceptor("UPDATE \"WorkspaceMembers\"");
        await using var taskContext = database.CreateContext(barrier);
        await using var removalContext = database.CreateContext(barrier);
        Task<IResult> taskOperation = operation == "assign"
            ? TaskCollaborationTestServices.TaskRequests(taskContext).AssignTaskAsync(1,
                new AssignTaskDto { AssigneeUserId = 3, Version = 0 }, 1)
            : TaskCollaborationTestServices.TaskRequests(taskContext).ReopenTaskAsync(1,
                new TaskWorkflowCommandDto { Version = 0 }, 1);
        var removal = Workspace(removalContext, 1).RemoveMemberAsync(1, 3,
            new WorkspaceVersionDto { Version = 0 });
        await Task.WhenAll(taskOperation, removal);

        var results = new[] { taskOperation.Result, removal.Result };
        Assert.Single(results, x => x.Success);
        Assert.IsAssignableFrom<IConflictResult>(Assert.Single(results, x => !x.Success));
        await using var verify = database.CreateContext();
        var task = await verify.TaskRequests.SingleAsync();
        var member = await verify.WorkspaceMembers.SingleAsync(x => x.UserId == 3);
        if (member.IsActive)
        {
            Assert.Equal(3, task.AssigneeUserId);
            Assert.Equal(TaskStatus.Pending, task.Status);
        }
        else
        {
            Assert.Equal(operation == "reopen" ? 3 : null, task.AssigneeUserId);
            Assert.Equal(operation == "reopen" ? TaskStatus.Completed : TaskStatus.Pending, task.Status);
        }
    }

    private static WorkspaceManager Workspace(TaskTrackerDbContext context, int userId)
    {
        var uow = new UnitOfWork(context);
        return new WorkspaceManager(uow, new EfWorkspaceDal(context), new CurrentUser(userId),
            TaskCollaborationTestServices.Create(context),
            NullLogger<WorkspaceManager>.Instance);
    }

    private sealed class CurrentUser(int id) : ICurrentUserService
    {
        public int UserId => id;
    }
}
