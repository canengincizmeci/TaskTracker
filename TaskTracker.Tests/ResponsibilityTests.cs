using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

public class ResponsibilityTests
{
    private sealed class CurrentUser(int id) : ICurrentUserService { public int UserId => id; }
    private sealed class NotificationRealtime : IRealtimeNotificationService
    {
        public Task SendNotificationAsync(int id, NotificationDto dto) => Task.CompletedTask;
    }
    private sealed class Email : IEmailService
    {
        public Task SendVerificationCodeAsync(string email, string code) => Task.CompletedTask;
        public Task SendPasswordResetCodeAsync(string email, string code) => Task.CompletedTask;
        public Task SendTaskShareInvitationEmailAsync(string email, string title, string user, string url) => Task.CompletedTask;
    }
    private sealed class RecordingWorkspace : ITaskWorkspaceService
    {
        public List<(int TaskId, int UserId)> Revocations { get; } = [];
        public Task<bool> CanAccessAsync(int taskId, int userId) => Task.FromResult(true);
        public Task<IDataResult<List<TaskActivityDto>>> GetActivitiesAsync(int taskId, int userId) =>
            throw new NotSupportedException();
        public Task<IDataResult<List<TaskMessageDto>>> GetMessagesAsync(int taskId, int userId) =>
            throw new NotSupportedException();
        public Task<IDataResult<TaskMessageDto>> SendMessageAsync(int taskId, int userId, string? content) =>
            throw new NotSupportedException();
        public Task PublishActivityAsync(TaskActivity activity) => Task.CompletedTask;
        public Task PublishTaskChangedAsync(int taskId) => Task.CompletedTask;
        public Task RevokeAccessAsync(int taskId, int userId)
        {
            Revocations.Add((taskId, userId));
            return Task.CompletedTask;
        }
    }

    private static TaskRequestManager Tasks(TaskTrackerDbContext context, int userId = 1, bool failRealtime = false)
    {
        var uow = new UnitOfWork(context);
        var current = new CurrentUser(userId);
        var notifications = new NotificationManager(uow, current, new NotificationRealtime(), NullLogger<NotificationManager>.Instance);
        return new TaskRequestManager(uow, new EfTaskShareDal(context), new EfTaskRequestDal(context),
            new TaskActivityWriter(uow), WorkspaceTestServices.Create(context, failRealtime), notifications,
            NullLogger<TaskRequestManager>.Instance);
    }

    private static TaskShareManager Shares(TaskTrackerDbContext context, int userId = 1,
        ITaskWorkspaceService? workspace = null)
    {
        var uow = new UnitOfWork(context);
        var current = new CurrentUser(userId);
        var notifications = new NotificationManager(uow, current, new NotificationRealtime(), NullLogger<NotificationManager>.Instance);
        return new TaskShareManager(uow, new EfTaskShareDal(context), current, new Email(), notifications,
            new ConfigurationBuilder().Build(), NullLogger<TaskShareManager>.Instance,
            new TaskActivityWriter(uow), workspace ?? WorkspaceTestServices.Create(context));
    }

    [Fact]
    public async Task Owner_assigns_only_owner_or_accepted_working_participant()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShares.Add(new() { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        context.TaskShares.Add(new() { TaskRequestId = 1, SharedWithUserId = 3, Permission = TaskPermission.View });
        await context.SaveChangesAsync();
        var manager = Tasks(context);

        Assert.True((await manager.AssignTaskAsync(1, new() { AssigneeUserId = 2, Version = 0 }, 1)).Success);
        Assert.Equal(2, (await context.TaskRequests.SingleAsync()).AssigneeUserId);
        var assignment = await context.TaskActivities.SingleAsync();
        Assert.Equal(TaskActivityType.UserAssigned, assignment.ActivityType);
        Assert.Equal(2, assignment.TargetUserId);
        var notification = await context.Notifications.SingleAsync();
        Assert.Equal("/tasks/task-detail/1", notification.RedirectUrl);

        Assert.False((await manager.AssignTaskAsync(1, new() { AssigneeUserId = 3, Version = 1 }, 1)).Success);
        Assert.False((await manager.AssignTaskAsync(1, new() { AssigneeUserId = 3, Version = 1 }, 2)).Success);
        Assert.Equal(2, (await context.TaskRequests.SingleAsync()).AssigneeUserId);
    }

    [Fact]
    public async Task Pending_invitee_and_unrelated_user_cannot_be_assigned()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShareInvitations.Add(new() { TaskRequestId = 1, InvitedByUserId = 1, InvitedUserId = 2, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
        Assert.False((await Tasks(context).AssignTaskAsync(1, new() { AssigneeUserId = 2, Version = 0 }, 1)).Success);
        Assert.False((await Tasks(context).AssignTaskAsync(1, new() { AssigneeUserId = 3, Version = 0 }, 1)).Success);
    }

    [Fact]
    public async Task Reassign_and_unassign_replace_responsibility_and_assigned_list()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShares.AddRange(
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit },
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 3, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
        var manager = Tasks(context);
        Assert.True((await manager.AssignTaskAsync(1, new() { AssigneeUserId = 2, Version = 0 }, 1)).Success);
        Assert.Single((await manager.GetAssignedTasksAsync(2)).Data);
        Assert.True((await manager.AssignTaskAsync(1, new() { AssigneeUserId = 3, Version = 1 }, 1)).Success);
        Assert.Empty((await manager.GetAssignedTasksAsync(2)).Data);
        Assert.Single((await manager.GetAssignedTasksAsync(3)).Data);
        Assert.True((await manager.UnassignTaskAsync(1, new() { Version = 2 }, 1)).Success);
        Assert.Null((await context.TaskRequests.SingleAsync()).AssigneeUserId);
        Assert.Empty((await manager.GetAssignedTasksAsync(3)).Data);
    }

    [Fact]
    public async Task Duplicate_assignment_is_idempotent_and_shared_work_is_not_misclassified()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShares.AddRange(
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit },
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 3, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
        var manager = Tasks(context);

        Assert.True((await manager.AssignTaskAsync(1, new() { AssigneeUserId = 2, Version = 0 }, 1)).Success);
        Assert.True((await manager.AssignTaskAsync(1, new() { AssigneeUserId = 2, Version = 1 }, 1)).Success);
        Assert.Single((await manager.GetAssignedTasksAsync(2)).Data);
        Assert.Empty((await manager.GetAssignedTasksAsync(3)).Data);
        Assert.Single(await context.TaskActivities.Where(x => x.ActivityType == TaskActivityType.UserAssigned).ToListAsync());
        Assert.Single(await context.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Assignee_starts_delegated_work_but_other_collaborator_cannot()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        var task = TestDatabase.Task(); task.AssigneeUserId = 2;
        context.TaskRequests.Add(task);
        context.TaskShares.AddRange(
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit },
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 3, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
        Assert.False((await Tasks(context).StartTaskAsync(1, new() { Version = 0 }, 3)).Success);
        Assert.True((await Tasks(context).StartTaskAsync(1, new() { Version = 0 }, 2)).Success);
        Assert.Equal(TaskStatus.InProgress, (await context.TaskRequests.SingleAsync()).Status);
        Assert.Equal(TaskActivityType.WorkStarted, (await context.TaskActivities.SingleAsync()).ActivityType);
        Assert.False((await Tasks(context).CompleteTaskAsync(1, new() { Version = 1 }, 1)).Success);
    }

    [Fact]
    public async Task Owner_starts_and_completes_unassigned_personal_work()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task()); await context.SaveChangesAsync();
        var manager = Tasks(context);
        Assert.True((await manager.StartTaskAsync(1, new() { Version = 0 }, 1)).Success);
        Assert.True((await manager.CompleteTaskAsync(1, new() { Version = 1 }, 1)).Success);
        Assert.Equal(TaskStatus.Completed, (await context.TaskRequests.SingleAsync()).Status);
        Assert.Contains(await context.TaskActivities.ToListAsync(), x =>
            x.ActivityType == TaskActivityType.TaskCompleted &&
            x.FromStatus == TaskStatus.InProgress && x.ToStatus == TaskStatus.Completed);
        Assert.False((await manager.StartTaskAsync(1, new() { Version = 2 }, 1)).Success);
    }

    [Fact]
    public async Task Owner_can_cancel_and_reopen_with_structured_history()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task()); await context.SaveChangesAsync();
        var manager = Tasks(context);

        Assert.True((await manager.CancelTaskAsync(1, new() { Version = 0 }, 1)).Success);
        Assert.True((await manager.ReopenTaskAsync(1, new() { Version = 1 }, 1)).Success);
        var task = await context.TaskRequests.SingleAsync();
        Assert.Equal(TaskStatus.Pending, task.Status);
        var history = await context.TaskActivities.OrderBy(x => x.Id).ToListAsync();
        Assert.Collection(history,
            x => Assert.Equal((TaskActivityType.TaskCancelled, TaskStatus.Pending, TaskStatus.Cancelled),
                (x.ActivityType, x.FromStatus, x.ToStatus)),
            x => Assert.Equal((TaskActivityType.TaskReopened, TaskStatus.Cancelled, TaskStatus.Pending),
                (x.ActivityType, x.FromStatus, x.ToStatus)));
    }

    [Fact]
    public async Task Stale_responsibility_command_cannot_overwrite_a_reassignment()
    {
        using var db = new TestDatabase(); using var firstContext = db.CreateContext();
        firstContext.TaskRequests.Add(TestDatabase.Task());
        firstContext.TaskShares.AddRange(
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit },
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 3, Permission = TaskPermission.Edit });
        await firstContext.SaveChangesAsync();
        Assert.True((await Tasks(firstContext).AssignTaskAsync(1,
            new() { AssigneeUserId = 2, Version = 0 }, 1)).Success);

        using var staleContext = db.CreateContext();
        await staleContext.TaskRequests.FindAsync(1);
        Assert.True((await Tasks(firstContext).AssignTaskAsync(1,
            new() { AssigneeUserId = 3, Version = 1 }, 1)).Success);
        Assert.False((await Tasks(staleContext).UnassignTaskAsync(1,
            new() { Version = 1 }, 1)).Success);

        using var verify = db.CreateContext();
        Assert.Equal(3, (await verify.TaskRequests.SingleAsync()).AssigneeUserId);
    }

    [Fact]
    public async Task Assignment_cannot_race_a_committed_participant_removal()
    {
        using var db = new TestDatabase(); using var staleContext = db.CreateContext();
        staleContext.TaskRequests.Add(TestDatabase.Task());
        staleContext.TaskShares.Add(new() { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        await staleContext.SaveChangesAsync();
        await staleContext.TaskRequests.FindAsync(1);

        using var ownerContext = db.CreateContext();
        Assert.True((await Shares(ownerContext).RemoveParticipantAsync(1, 2, 0)).Success);
        Assert.False((await Tasks(staleContext).AssignTaskAsync(1,
            new() { AssigneeUserId = 2, Version = 0 }, 1)).Success);

        using var verify = db.CreateContext();
        Assert.Null((await verify.TaskRequests.SingleAsync()).AssigneeUserId);
        Assert.Empty(await verify.TaskShares.ToListAsync());
    }

    [Fact]
    public async Task Stale_detail_edit_cannot_overwrite_workflow_transition()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task()); await context.SaveChangesAsync();
        var manager = Tasks(context);
        Assert.True((await manager.StartTaskAsync(1, new() { Version = 0 }, 1)).Success);
        var edit = new UpdateTaskRequestDto
        { Id = 1, Version = 0, Title = "Stale title", Description = "Stale details", Category = "Tests", Priority = TaskPriority.Medium };
        Assert.False((await manager.UpdateTask(edit, 1)).Success);
        var task = await context.TaskRequests.SingleAsync();
        Assert.Equal(TaskStatus.InProgress, task.Status);
        Assert.Equal("Workflow task", task.Title);
    }

    [Fact]
    public async Task Downgrade_or_removal_unassigns_participant_atomically()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        var task = TestDatabase.Task(); task.AssigneeUserId = 2; task.Status = TaskStatus.InProgress;
        context.TaskRequests.Add(task);
        context.TaskShares.Add(new() { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
        var manager = Shares(context);
        Assert.True((await manager.UpdateParticipantPermissionAsync(1, 2,
            new() { Permission = TaskPermission.View, Version = 0 })).Success);
        Assert.Null(task.AssigneeUserId); Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.True((await manager.RemoveParticipantAsync(1, 2, 1)).Success);
        Assert.Empty(await context.TaskShares.ToListAsync());
    }

    [Fact]
    public async Task Owner_cancels_pending_invitation_and_recipient_cannot_accept_it()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShareInvitations.Add(new() { Id = 1, TaskRequestId = 1, InvitedByUserId = 1, InvitedUserId = 2, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
        Assert.True((await Shares(context).CancelInvitationAsync(1, 0)).Success);
        Assert.Equal(TaskShareInvitationStatus.Cancelled, (await context.TaskShareInvitations.SingleAsync()).Status);
        Assert.False((await Shares(context, 2).AcceptTaskInvitationAsync(1)).Success);
        Assert.Empty(await context.TaskShares.ToListAsync());
    }

    [Fact]
    public async Task Accept_loaded_before_owner_cancellation_cannot_win_after_cancellation_commits()
    {
        using var db = new TestDatabase(); using var ownerContext = db.CreateContext();
        ownerContext.TaskRequests.Add(TestDatabase.Task());
        ownerContext.TaskShareInvitations.Add(new() { Id = 1, TaskRequestId = 1, InvitedByUserId = 1, InvitedUserId = 2, Permission = TaskPermission.Edit });
        await ownerContext.SaveChangesAsync();
        using var recipientContext = db.CreateContext();
        await recipientContext.TaskRequests.FindAsync(1);
        await recipientContext.TaskShareInvitations.FindAsync(1);
        Assert.True((await Shares(ownerContext).CancelInvitationAsync(1, 0)).Success);
        Assert.False((await Shares(recipientContext, 2).AcceptTaskInvitationAsync(1)).Success);
        using var verify = db.CreateContext();
        Assert.Equal(TaskShareInvitationStatus.Cancelled, (await verify.TaskShareInvitations.SingleAsync()).Status);
        Assert.Empty(await verify.TaskShares.ToListAsync());
    }

    [Fact]
    public async Task Non_owner_cannot_change_permission_and_owner_cannot_be_removed()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShares.Add(new() { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
        Assert.False((await Shares(context, 2).UpdateParticipantPermissionAsync(1, 2,
            new() { Permission = TaskPermission.Manage, Version = 0 })).Success);
        Assert.False((await Shares(context).RemoveParticipantAsync(1, 1, 0)).Success);
        Assert.Single(await context.TaskShares.ToListAsync());
    }

    [Fact]
    public async Task Removed_participant_loses_task_and_workspace_access()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShares.Add(new() { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
        Assert.True(await new EfTaskWorkspaceDal(context).CanAccessAsync(1, 2));
        var realtime = new RecordingWorkspace();
        Assert.True((await Shares(context, workspace: realtime).RemoveParticipantAsync(1, 2, 0)).Success);
        Assert.False(await new EfTaskWorkspaceDal(context).CanAccessAsync(1, 2));
        Assert.False((await Tasks(context).GetTaskById(1, 2)).Success);
        Assert.Contains((1, 2), realtime.Revocations);
        Assert.Equal("/tasks/shared-tasks", (await context.Notifications.SingleAsync()).RedirectUrl);
    }

    [Fact]
    public async Task Realtime_failure_does_not_undo_assignment()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShares.Add(new() { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
        Assert.True((await Tasks(context, failRealtime: true).AssignTaskAsync(1,
            new() { AssigneeUserId = 2, Version = 0 }, 1)).Success);
        Assert.Equal(2, (await context.TaskRequests.SingleAsync()).AssigneeUserId);
    }

    [Fact]
    public async Task In_review_blocks_assignee_permission_downgrade()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        var task = TestDatabase.Task(); task.AssigneeUserId = 2; task.Status = TaskStatus.InReview;
        context.TaskRequests.Add(task);
        context.TaskShares.Add(new() { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();

        var result = await Shares(context).UpdateParticipantPermissionAsync(1, 2,
            new() { Permission = TaskPermission.View, Version = 0 });

        Assert.IsAssignableFrom<IConflictResult>(result);
        Assert.Equal(2, task.AssigneeUserId);
        Assert.Equal(TaskPermission.Edit, (await context.TaskShares.SingleAsync()).Permission);
    }
}
