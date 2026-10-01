using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class TaskLifecycleHistoryTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(null)]
    public async Task Workspace_creation_records_initial_assignment_only_when_present(int? assigneeId)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);

        var result = await TaskCollaborationTestServices.TaskRequests(context)
            .AddWorkspaceTaskAsync(workspaceId, CreateWorkspaceTask(assigneeId), 1);

        Assert.True(result.Success);
        var activities = await OrderedActivities(context);
        if (assigneeId.HasValue)
        {
            AssertSingleOperationOrder(activities);
            Assert.Collection(activities,
                activity => Assert.Equal(TaskActivityType.TaskCreated, activity.ActivityType),
                activity =>
                {
                    Assert.Equal(TaskActivityType.UserAssigned, activity.ActivityType);
                    Assert.Equal(assigneeId, activity.TargetUserId);
                    Assert.Equal(1, activity.ActorUserId);
                    Assert.Null(activity.FromStatus);
                    Assert.Null(activity.ToStatus);
                });
        }
        else
        {
            Assert.Single(activities);
            Assert.Equal(TaskActivityType.TaskCreated, activities[0].ActivityType);
        }
    }

    [Fact]
    public async Task Workspace_creation_rolls_back_task_revision_and_activities_together()
    {
        using var database = new TestDatabase();
        await using (var seed = database.CreateContext()) await SeedWorkspace(seed);
        await using (var failing = database.CreateContext(new FailActivityInsertInterceptor()))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => TaskCollaborationTestServices.TaskRequests(failing)
                .AddWorkspaceTaskAsync(1, CreateWorkspaceTask(2), 1));
        }

        await using var verify = database.CreateContext();
        Assert.Empty(await verify.TaskRequests.ToListAsync());
        Assert.Empty(await verify.TaskPlanningRevisions.ToListAsync());
        Assert.Empty(await verify.TaskActivities.ToListAsync());
    }

    [Theory]
    [InlineData(TaskStatus.InProgress, true)]
    [InlineData(TaskStatus.Pending, false)]
    public async Task Reassignment_records_ordered_assignment_events_and_only_real_reset(
        TaskStatus initialStatus, bool expectsReset)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalResponsibility(context, initialStatus);

        var result = await TaskCollaborationTestServices.TaskRequests(context)
            .AssignTaskAsync(1, new AssignTaskDto { AssigneeUserId = 3, Version = 0 }, 1);

        Assert.True(result.Success);
        var task = await context.TaskRequests.SingleAsync();
        Assert.Equal(3, task.AssigneeUserId);
        Assert.Equal(TaskStatus.Pending, task.Status);
        var activities = await OrderedActivities(context);
        AssertSingleOperationOrder(activities);
        var expectedTypes = expectsReset
            ? new[] { TaskActivityType.UserUnassigned, TaskActivityType.ResponsibilityReset, TaskActivityType.UserAssigned }
            : new[] { TaskActivityType.UserUnassigned, TaskActivityType.UserAssigned };
        Assert.Equal(expectedTypes, activities.Select(x => x.ActivityType));
        Assert.Equal(2, activities[0].TargetUserId);
        Assert.Equal(3, activities[^1].TargetUserId);
        AssertReset(activities, expectsReset);
    }

    [Theory]
    [InlineData(TaskStatus.InProgress, true)]
    [InlineData(TaskStatus.Pending, false)]
    public async Task Unassignment_records_user_event_before_only_real_reset(
        TaskStatus initialStatus, bool expectsReset)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalResponsibility(context, initialStatus);

        var result = await TaskCollaborationTestServices.TaskRequests(context)
            .UnassignTaskAsync(1, new TaskWorkflowCommandDto { Version = 0 }, 1);

        Assert.True(result.Success);
        var task = await context.TaskRequests.SingleAsync();
        Assert.Null(task.AssigneeUserId);
        Assert.Equal(TaskStatus.Pending, task.Status);
        var activities = await OrderedActivities(context);
        AssertSingleOperationOrder(activities);
        Assert.Equal(expectsReset
                ? new[] { TaskActivityType.UserUnassigned, TaskActivityType.ResponsibilityReset }
                : new[] { TaskActivityType.UserUnassigned },
            activities.Select(x => x.ActivityType));
        Assert.Equal(2, activities[0].TargetUserId);
        AssertReset(activities, expectsReset);
    }

    [Theory]
    [InlineData(false, TaskActivityType.ParticipantPermissionChanged)]
    [InlineData(true, TaskActivityType.ParticipantRemoved)]
    public async Task Losing_assignee_access_records_unassignment_reset_then_participant_event(
        bool removeParticipant, TaskActivityType participantEvent)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalResponsibility(context, TaskStatus.InProgress, includeThirdUser: false);
        var manager = TaskCollaborationTestServices.TaskShares(context, 1);

        var result = removeParticipant
            ? await manager.RemoveParticipantAsync(1, 2, 0)
            : await manager.UpdateParticipantPermissionAsync(1, 2,
                new UpdateParticipantPermissionDto { Permission = TaskPermission.View, Version = 0 });

        Assert.True(result.Success);
        var task = await context.TaskRequests.SingleAsync();
        Assert.Null(task.AssigneeUserId);
        Assert.Equal(TaskStatus.Pending, task.Status);
        var activities = await OrderedActivities(context);
        AssertSingleOperationOrder(activities);
        Assert.Equal(new[]
        {
            TaskActivityType.UserUnassigned,
            TaskActivityType.ResponsibilityReset,
            participantEvent
        }, activities.Select(x => x.ActivityType));
        AssertReset(activities, true);
    }

    [Fact]
    public async Task Ordered_activity_stream_reconstructs_assignment_and_status_timeline()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);
        var manager = TaskCollaborationTestServices.TaskRequests(context);
        var created = await manager.AddWorkspaceTaskAsync(workspaceId, CreateWorkspaceTask(2), 1);
        Assert.True(created.Success);
        var taskId = created.Data!.Id!.Value;

        Assert.True((await manager.StartTaskAsync(taskId, new TaskWorkflowCommandDto { Version = 0 }, 2)).Success);
        Assert.True((await manager.AssignTaskAsync(taskId,
            new AssignTaskDto { AssigneeUserId = 3, Version = 1 }, 1)).Success);
        Assert.True((await manager.StartTaskAsync(taskId, new TaskWorkflowCommandDto { Version = 2 }, 3)).Success);
        Assert.True((await manager.UnassignTaskAsync(taskId,
            new TaskWorkflowCommandDto { Version = 3 }, 1)).Success);

        int? reconstructedAssignee = null;
        var reconstructedStatus = TaskStatus.Pending;
        var assignmentTimeline = new List<int?>();
        foreach (var activity in await OrderedActivities(context, taskId))
        {
            if (activity.ActivityType == TaskActivityType.UserAssigned)
            {
                reconstructedAssignee = activity.TargetUserId;
                assignmentTimeline.Add(reconstructedAssignee);
            }
            else if (activity.ActivityType == TaskActivityType.UserUnassigned)
            {
                Assert.Equal(reconstructedAssignee, activity.TargetUserId);
                reconstructedAssignee = null;
                assignmentTimeline.Add(null);
            }

            if (activity.FromStatus.HasValue)
            {
                Assert.Equal(reconstructedStatus, activity.FromStatus);
                reconstructedStatus = activity.ToStatus!.Value;
            }
        }

        Assert.Equal(new int?[] { 2, null, 3, null }, assignmentTimeline);
        Assert.Null(reconstructedAssignee);
        Assert.Equal(TaskStatus.Pending, reconstructedStatus);
        var stored = await context.TaskRequests.SingleAsync(x => x.Id == taskId);
        Assert.Equal(stored.AssigneeUserId, reconstructedAssignee);
        Assert.Equal(stored.Status, reconstructedStatus);
    }

    [Fact]
    public async Task Stale_reassignment_cannot_leave_orphan_reset_or_assignment_events()
    {
        using var database = new TestDatabase();
        await using (var seed = database.CreateContext())
            await SeedPersonalResponsibility(seed, TaskStatus.InProgress);
        await using var stale = database.CreateContext();
        await stale.TaskRequests.FindAsync(1);
        await using (var winner = database.CreateContext())
        {
            var result = await TaskCollaborationTestServices.TaskRequests(winner)
                .AssignTaskAsync(1, new AssignTaskDto { AssigneeUserId = 3, Version = 0 }, 1);
            Assert.True(result.Success);
        }

        var staleResult = await TaskCollaborationTestServices.TaskRequests(stale)
            .UnassignTaskAsync(1, new TaskWorkflowCommandDto { Version = 0 }, 1);
        Assert.False(staleResult.Success);

        await using var verify = database.CreateContext();
        var task = await verify.TaskRequests.SingleAsync();
        Assert.Equal(3, task.AssigneeUserId);
        Assert.Equal(TaskStatus.Pending, task.Status);
        var activities = await OrderedActivities(verify);
        Assert.Equal(new[]
        {
            TaskActivityType.UserUnassigned,
            TaskActivityType.ResponsibilityReset,
            TaskActivityType.UserAssigned
        }, activities.Select(x => x.ActivityType));
    }

    private static void AssertReset(IReadOnlyCollection<TaskActivity> activities, bool expected)
    {
        var reset = activities.SingleOrDefault(x => x.ActivityType == TaskActivityType.ResponsibilityReset);
        if (!expected)
        {
            Assert.Null(reset);
            return;
        }

        Assert.NotNull(reset);
        Assert.Equal(1, reset.ActorUserId);
        Assert.Equal(TaskStatus.InProgress, reset.FromStatus);
        Assert.Equal(TaskStatus.Pending, reset.ToStatus);
    }

    private static void AssertSingleOperationOrder(IReadOnlyList<TaskActivity> activities)
    {
        Assert.Single(activities.Select(x => x.CreatedAt).Distinct());
        Assert.True(activities.Zip(activities.Skip(1), (first, second) => first.Id < second.Id).All(x => x));
    }

    private static Task<List<TaskActivity>> OrderedActivities(TaskTrackerDbContext context, int taskId = 1) =>
        context.TaskActivities.Where(x => x.TaskRequestId == taskId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync();

    private static async Task SeedPersonalResponsibility(TaskTrackerDbContext context, TaskStatus status,
        bool includeThirdUser = true)
    {
        var task = TestDatabase.Task();
        task.AssigneeUserId = 2;
        task.Status = status;
        context.TaskRequests.Add(task);
        context.TaskShares.Add(new TaskShare
            { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        if (includeThirdUser)
            context.TaskShares.Add(new TaskShare
                { TaskRequestId = 1, SharedWithUserId = 3, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
    }

    private static async Task<int> SeedWorkspace(TaskTrackerDbContext context)
    {
        var workspace = new Workspace
        {
            Name = "Lifecycle",
            Members =
            [
                new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner },
                new WorkspaceMember { UserId = 2, Role = WorkspaceRole.Member },
                new WorkspaceMember { UserId = 3, Role = WorkspaceRole.Member }
            ]
        };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();
        return workspace.Id;
    }

    private static WorkspaceTaskCreateDto CreateWorkspaceTask(int? assigneeId) => new()
    {
        Title = "Lifecycle task",
        Description = "Lifecycle history test",
        Category = "Tests",
        Priority = TaskPriority.Medium,
        Status = TaskStatus.Pending,
        AssigneeUserId = assigneeId
    };

    private sealed class FailActivityInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            command.CommandText.Contains("INSERT INTO \"TaskActivities\"", StringComparison.OrdinalIgnoreCase)
                ? ValueTask.FromException<InterceptionResult<DbDataReader>>(
                    new DbUpdateException("Simulated activity insert failure."))
                : ValueTask.FromResult(result);
    }
}
