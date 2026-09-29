using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

public class WorkspaceRealtimeRevocationTests
{
    [Fact]
    public async Task Successful_removal_revokes_each_active_task_in_only_the_target_workspace()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var (workspaceId, otherWorkspaceId) = await SeedWorkspacesAsync(context);
        var first = CreateTask(11, workspaceId, activity: true, TaskStatus.Pending);
        var second = CreateTask(12, workspaceId, activity: true, TaskStatus.InProgress);
        context.TaskRequests.AddRange(
            first,
            second,
            CreateTask(13, workspaceId, activity: false, TaskStatus.Pending),
            CreateTask(14, otherWorkspaceId, activity: true, TaskStatus.Pending),
            CreateTask(15, null, activity: true, TaskStatus.Pending));
        await context.SaveChangesAsync();
        var target = await context.WorkspaceMembers.SingleAsync(x =>
            x.WorkspaceId == workspaceId && x.UserId == 2);
        var collaboration = new RecordingCollaboration();

        var result = await Manager(context, collaboration).RemoveMemberAsync(workspaceId, 2,
            new WorkspaceVersionDto { Version = target.Version });

        Assert.True(result.Success);
        Assert.False(target.IsActive);
        Assert.NotNull(target.RemovedAt);
        Assert.Equal(
            [(first.Id, 2), (second.Id, 2)],
            collaboration.Revocations.OrderBy(x => x.TaskId).ToList());
    }

    [Fact]
    public async Task Persistence_failure_does_not_revoke_realtime_access()
    {
        using var database = new TestDatabase();
        int workspaceId;
        await using (var setup = database.CreateContext())
        {
            (workspaceId, _) = await SeedWorkspacesAsync(setup);
            setup.TaskRequests.Add(CreateTask(11, workspaceId, activity: true, TaskStatus.Pending));
            await setup.SaveChangesAsync();
        }

        var collaboration = new RecordingCollaboration();
        await using (var context = database.CreateContext(new FailingSaveInterceptor()))
        {
            var target = await context.WorkspaceMembers.SingleAsync(x =>
                x.WorkspaceId == workspaceId && x.UserId == 2);

            var result = await Manager(context, collaboration).RemoveMemberAsync(workspaceId, 2,
                new WorkspaceVersionDto { Version = target.Version });

            Assert.False(result.Success);
            Assert.IsAssignableFrom<IConflictResult>(result);
            Assert.Empty(collaboration.Revocations);
        }

        await using var verify = database.CreateContext();
        Assert.True((await verify.WorkspaceMembers.SingleAsync(x =>
            x.WorkspaceId == workspaceId && x.UserId == 2)).IsActive);
    }

    [Fact]
    public async Task Realtime_failure_does_not_fail_removal_or_stop_remaining_revocations()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var (workspaceId, _) = await SeedWorkspacesAsync(context);
        var first = CreateTask(11, workspaceId, activity: true, TaskStatus.Pending);
        var second = CreateTask(12, workspaceId, activity: true, TaskStatus.Pending);
        context.TaskRequests.AddRange(first, second);
        await context.SaveChangesAsync();
        var target = await context.WorkspaceMembers.SingleAsync(x =>
            x.WorkspaceId == workspaceId && x.UserId == 2);
        var collaboration = new RecordingCollaboration([first.Id]);

        var result = await Manager(context, collaboration).RemoveMemberAsync(workspaceId, 2,
            new WorkspaceVersionDto { Version = target.Version });

        Assert.True(result.Success);
        Assert.False(target.IsActive);
        Assert.Equal(
            new[] { first.Id, second.Id },
            collaboration.Revocations.Select(x => x.TaskId).OrderBy(x => x).ToArray());
    }

    private static WorkspaceManager Manager(TaskTrackerDbContext context,
        ITaskCollaborationService collaboration) =>
        new(new UnitOfWork(context), new EfWorkspaceDal(context), new CurrentUser(1), collaboration,
            NullLogger<WorkspaceManager>.Instance, new IdentityNormalizer());

    private static async Task<(int WorkspaceId, int OtherWorkspaceId)> SeedWorkspacesAsync(
        TaskTrackerDbContext context)
    {
        var workspace = new Workspace
        {
            Name = "Target workspace",
            Members =
            [
                new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner },
                new WorkspaceMember { UserId = 2, Role = WorkspaceRole.Member }
            ]
        };
        var other = new Workspace
        {
            Name = "Other workspace",
            Members = [new WorkspaceMember { UserId = 3, Role = WorkspaceRole.Owner }]
        };
        context.Workspaces.AddRange(workspace, other);
        await context.SaveChangesAsync();
        return (workspace.Id, other.Id);
    }

    private static TaskRequest CreateTask(int id, int? workspaceId, bool activity, TaskStatus status) => new()
    {
        Id = id,
        OwnerId = 1,
        WorkspaceId = workspaceId,
        Title = $"Task {id}",
        Description = "Realtime revocation test",
        Category = "Tests",
        Activity = activity,
        Status = status
    };

    private sealed class CurrentUser(int id) : ICurrentUserService
    {
        public int UserId => id;
    }

    private sealed class RecordingCollaboration(IEnumerable<int>? failingTaskIds = null)
        : ITaskCollaborationService
    {
        private readonly HashSet<int> failingTaskIds = failingTaskIds?.ToHashSet() ?? [];
        public List<(int TaskId, int UserId)> Revocations { get; } = [];

        public Task<bool> CanAccessAsync(int taskId, int userId) => throw new NotSupportedException();
        public Task<IDataResult<List<TaskActivityDto>>> GetActivitiesAsync(int taskId, int userId) =>
            throw new NotSupportedException();
        public Task<IDataResult<List<TaskMessageDto>>> GetMessagesAsync(int taskId, int userId) =>
            throw new NotSupportedException();
        public Task<IDataResult<TaskMessageDto>> SendMessageAsync(int taskId, int userId, string? content) =>
            throw new NotSupportedException();
        public Task PublishActivityAsync(TaskActivity activity) => throw new NotSupportedException();
        public Task PublishTaskChangedAsync(int taskId) => throw new NotSupportedException();

        public Task RevokeAccessAsync(int taskId, int userId)
        {
            Revocations.Add((taskId, userId));
            return failingTaskIds.Contains(taskId)
                ? Task.FromException(new IOException("Realtime delivery unavailable"))
                : Task.CompletedTask;
        }
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(
                new DbUpdateException("Simulated persistence failure"));
    }
}
