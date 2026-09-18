using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class InvitationTests
{
    private sealed class BeforeSave : SaveChangesInterceptor
    {
        public Func<Task>? Action { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Action is not null) { var action = Action; Action = null; await action(); }
            return result;
        }
    }
    private sealed class CurrentUser(int id) : ICurrentUserService { public int UserId => id; }
    private sealed class Email : IEmailService
    {
        public Task SendVerificationCodeAsync(string email, string code) => Task.CompletedTask;
        public Task SendPasswordResetCodeAsync(string email, string code) => Task.CompletedTask;
        public Task SendTaskShareInvitationEmailAsync(string email, string title, string user, string url) => Task.CompletedTask;
    }
    private sealed class Realtime(bool fail) : IRealtimeNotificationService
    {
        public Task SendNotificationAsync(int id, NotificationDto dto) => fail
            ? Task.FromException(new IOException("Simulated delivery failure")) : Task.CompletedTask;
    }
    private static TaskShareManager Manager(TaskTrackerDbContext context, int userId = 2, bool failRealtime = false)
    {
        var uow = new UnitOfWork(context);
        var user = new CurrentUser(userId);
        return new TaskShareManager(uow, new EfTaskShareDal(context), user, new Email(),
            new NotificationManager(uow, user, new Realtime(failRealtime), NullLogger<NotificationManager>.Instance),
            new ConfigurationBuilder().Build(), NullLogger<TaskShareManager>.Instance,
            new TaskActivityWriter(uow), WorkspaceTestServices.Create(context));
    }
    private static async Task Seed(TaskTrackerDbContext context, bool expired = false, TaskPermission permission = TaskPermission.Edit)
    {
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShareInvitations.Add(new TaskShareInvitation
        {
            Id = 1, TaskRequestId = 1, InvitedByUserId = 1, InvitedUserId = 2, Permission = permission,
            ExpiresAt = DateTime.UtcNow.AddDays(expired ? -1 : 1)
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Expired_invitation_is_not_actionable_and_does_not_block_reinvitation()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await Seed(context, expired: true);
        var recipient = Manager(context);
        Assert.Empty((await recipient.GetMyPendingInvitationsAsync()).Data);
        Assert.Equal("Expired", (await recipient.GetInvitationAsync(1)).Data.Status);
        Assert.False((await recipient.AcceptTaskInvitationAsync(1)).Success);
        Assert.False((await recipient.RejectTaskInvitationAsync(1)).Success);
        Assert.True((await Manager(context, 1).InviteUserToTask(new() { TaskRequestId = 1, Username = "user2", Permission = TaskPermission.Edit })).Success);
        Assert.Single((await recipient.GetMyPendingInvitationsAsync()).Data);
        Assert.Equal(TaskShareInvitationStatus.Pending, (await context.TaskShareInvitations.FindAsync(1))!.Status);
        Assert.Null((await context.TaskShareInvitations.FindAsync(1))!.RespondedAt);
    }

    [Fact]
    public async Task Acceptance_persists_membership_response_and_access_and_retry_is_idempotent()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await Seed(context);
        var access = WorkspaceTestServices.TaskRequests(context);
        Assert.False((await access.GetTaskById(1, 2)).Success);
        Assert.False((await Manager(context).GetParticipantsAsync(1)).Success);
        Assert.True((await Manager(context).AcceptTaskInvitationAsync(1)).Success);
        using var fresh = db.CreateContext();
        var recipient = Manager(fresh);
        var invitation = await fresh.TaskShareInvitations.SingleAsync();
        var respondedAt = invitation.RespondedAt;
        Assert.NotNull(respondedAt);
        Assert.Equal(TaskShareInvitationStatus.Accepted, invitation.Status);
        Assert.True((await recipient.AcceptTaskInvitationAsync(1)).Success);
        Assert.False((await recipient.RejectTaskInvitationAsync(1)).Success);
        Assert.Equal(respondedAt, invitation.RespondedAt);
        var share = Assert.Single(await fresh.TaskShares.ToListAsync());
        Assert.Equal(TaskPermission.Edit, share.Permission);
        Assert.NotNull(share.SharedAt);
        Assert.Empty((await recipient.GetMyPendingInvitationsAsync()).Data);
        Assert.Equal("Edit", Assert.Single((await recipient.GetMySharedTasksAsync()).Data).Permission);
        var task = await WorkspaceTestServices.TaskRequests(fresh).GetTaskById(1, 2);
        Assert.True(task.Success); Assert.True(task.Data.CanEdit); Assert.True(task.Data.CanViewParticipants);
        Assert.Equal(2, Assert.Single((await recipient.GetParticipantsAsync(1)).Data).UserId);
        Assert.Equal("Edit", Assert.Single((await Manager(fresh, 1).GetParticipantsAsync(1)).Data).Permission);
        Assert.Equal(TaskActivityType.InvitationAccepted, (await fresh.TaskActivities.SingleAsync()).ActivityType);
    }

    [Fact]
    public async Task Rejection_persists_response_without_membership_and_retry_is_idempotent()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext(); await Seed(context);
        Assert.True((await Manager(context).RejectTaskInvitationAsync(1)).Success);
        using var fresh = db.CreateContext(); var manager = Manager(fresh);
        var invitation = await fresh.TaskShareInvitations.SingleAsync();
        Assert.Equal(TaskShareInvitationStatus.Rejected, invitation.Status);
        Assert.NotNull(invitation.RespondedAt);
        var timestamp = invitation.RespondedAt;
        Assert.True((await manager.RejectTaskInvitationAsync(1)).Success);
        Assert.False((await manager.AcceptTaskInvitationAsync(1)).Success);
        Assert.Equal(timestamp, invitation.RespondedAt);
        Assert.Empty(await fresh.TaskShares.ToListAsync());
        Assert.Empty((await manager.GetMyPendingInvitationsAsync()).Data);
        Assert.False(await new EfTaskShareDal(fresh).HasPermissionAsync(1, 2, TaskPermission.View));
        Assert.Equal(TaskActivityType.InvitationRejected, (await fresh.TaskActivities.SingleAsync()).ActivityType);
    }

    [Theory]
    [InlineData(0, null)] [InlineData(1, "View")] [InlineData(2, "Edit")] [InlineData(3, "Manage")] [InlineData(99, null)]
    public async Task Permission_contract_uses_names_and_invalid_history_is_not_normalized(int value, string? expected)
    {
        using var db = new TestDatabase(); using var context = db.CreateContext(); await Seed(context, permission: (TaskPermission)value);
        var manager = Manager(context);
        var dto = Assert.Single((await manager.GetMyPendingInvitationsAsync()).Data);
        Assert.Equal(expected, dto.Permission);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(expected, json.RootElement.GetProperty("permission").GetString());
        Assert.Equal(expected is not null, dto.CanAccept);
        Assert.Equal(expected is not null, (await manager.AcceptTaskInvitationAsync(1)).Success);
        if (expected is null)
        {
            Assert.Empty(await context.TaskShares.ToListAsync());
            Assert.Equal((TaskPermission)value, (await context.TaskShareInvitations.SingleAsync()).Permission);
            Assert.True((await manager.RejectTaskInvitationAsync(1)).Success);
        }
    }

    [Fact]
    public async Task Participants_excludes_pending_invitees_and_denies_unrelated_users()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext(); await Seed(context);
        Assert.Empty((await Manager(context, 1).GetParticipantsAsync(1)).Data);
        Assert.False((await Manager(context, 3).GetParticipantsAsync(1)).Success);
        Assert.False((await Manager(context, 3).GetInvitationAsync(1)).Success);
        Assert.False((await Manager(context, 3).AcceptTaskInvitationAsync(1)).Success);
        Assert.False((await Manager(context, 3).RejectTaskInvitationAsync(1)).Success);
        Assert.True((await Manager(context).AcceptTaskInvitationAsync(1)).Success);
        context.TaskShareInvitations.Add(new() { TaskRequestId = 1, InvitedByUserId = 1, InvitedUserId = 3, Permission = TaskPermission.View });
        await context.SaveChangesAsync();
        Assert.Equal(2, Assert.Single((await Manager(context, 1).GetParticipantsAsync(1)).Data).UserId);
        Assert.False((await Manager(context, 3).GetParticipantsAsync(1)).Success);
    }

    [Theory]
    [InlineData(false, TaskStatus.Pending)] [InlineData(true, TaskStatus.Completed)] [InlineData(true, TaskStatus.Cancelled)]
    public async Task Inactive_and_terminal_tasks_cannot_receive_invitations_or_new_members(bool active, TaskStatus status)
    {
        using var db = new TestDatabase(); using var context = db.CreateContext(); await Seed(context);
        var task = await context.TaskRequests.SingleAsync(); task.Activity = active; task.Status = status;
        await context.SaveChangesAsync();
        Assert.False((await Manager(context).AcceptTaskInvitationAsync(1)).Success);
        Assert.Empty((await Manager(context).GetMyPendingInvitationsAsync()).Data);
        Assert.False((await Manager(context, 1).InviteUserToTask(new() { TaskRequestId = 1, Username = "user3", Permission = TaskPermission.Edit })).Success);
        Assert.Empty(await context.TaskShares.ToListAsync());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Competing_response_cannot_overwrite_a_committed_decision(bool firstAccepts)
    {
        using var db = new TestDatabase(); using var first = db.CreateContext(); await Seed(first);
        using var second = db.CreateContext();
        await second.TaskRequests.FindAsync(1); await second.TaskShareInvitations.FindAsync(1);
        var winner = Manager(first); var loser = Manager(second);
        Assert.True((await (firstAccepts ? winner.AcceptTaskInvitationAsync(1) : winner.RejectTaskInvitationAsync(1))).Success);
        Assert.False((await (firstAccepts ? loser.RejectTaskInvitationAsync(1) : loser.AcceptTaskInvitationAsync(1))).Success);
        using var fresh = db.CreateContext();
        Assert.Equal(firstAccepts ? TaskShareInvitationStatus.Accepted : TaskShareInvitationStatus.Rejected,
            (await fresh.TaskShareInvitations.SingleAsync()).Status);
        Assert.Equal(firstAccepts ? 1 : 0, await fresh.TaskShares.CountAsync());
    }

    [Fact]
    public async Task Realtime_failure_does_not_fail_persisted_invitation_or_notification()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task()); await context.SaveChangesAsync();
        var result = await Manager(context, 1, failRealtime: true).InviteUserToTask(new()
        { TaskRequestId = 1, Username = "user2", Permission = TaskPermission.Edit });
        Assert.True(result.Success);
        using var fresh = db.CreateContext();
        Assert.Single(await fresh.TaskShareInvitations.ToListAsync());
        Assert.Single(await fresh.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData(true, true)] [InlineData(true, false)] [InlineData(false, true)]
    public async Task Response_committed_during_competing_save_rolls_back_losing_response(bool winnerAccepts, bool loserAccepts)
    {
        using var db = new TestDatabase(); using var winnerContext = db.CreateContext(); await Seed(winnerContext);
        var barrier = new BeforeSave(); using var loserContext = db.CreateContext(barrier);
        barrier.Action = async () => Assert.True((await (winnerAccepts
            ? Manager(winnerContext).AcceptTaskInvitationAsync(1) : Manager(winnerContext).RejectTaskInvitationAsync(1))).Success);
        var loser = Manager(loserContext);
        Assert.False((await (loserAccepts ? loser.AcceptTaskInvitationAsync(1) : loser.RejectTaskInvitationAsync(1))).Success);
        using var fresh = db.CreateContext();
        Assert.Equal(winnerAccepts ? TaskShareInvitationStatus.Accepted : TaskShareInvitationStatus.Rejected,
            (await fresh.TaskShareInvitations.SingleAsync()).Status);
        Assert.Equal(winnerAccepts ? 1 : 0, await fresh.TaskShares.CountAsync());
    }

    [Fact]
    public async Task Concurrent_invitations_create_only_one_pending_record()
    {
        using var db = new TestDatabase(); using var winner = db.CreateContext();
        winner.TaskRequests.Add(TestDatabase.Task()); await winner.SaveChangesAsync();
        var barrier = new BeforeSave(); using var loser = db.CreateContext(barrier);
        var dto = new InviteUserToTaskDto { TaskRequestId = 1, Username = "user2", Permission = TaskPermission.Edit };
        barrier.Action = async () => Assert.True((await Manager(winner, 1).InviteUserToTask(dto)).Success);
        Assert.False((await Manager(loser, 1).InviteUserToTask(dto)).Success);
        using var fresh = db.CreateContext(); Assert.Single(await fresh.TaskShareInvitations.ToListAsync());
    }

    [Theory]
    [InlineData(0)] [InlineData(99)]
    public async Task Invalid_historical_membership_does_not_grant_access(int value)
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShares.Add(new() { TaskRequestId = 1, SharedWithUserId = 2, Permission = (TaskPermission)value });
        await context.SaveChangesAsync();
        var tasks = WorkspaceTestServices.TaskRequests(context);
        Assert.False((await tasks.GetTaskById(1, 2)).Success);
        Assert.Empty((await tasks.GetTasksByUserId(2)).Data);
        Assert.False((await Manager(context).GetParticipantsAsync(1)).Success);
        Assert.Null(Assert.Single((await Manager(context, 1).GetParticipantsAsync(1)).Data).Permission);
        Assert.Empty((await Manager(context).GetMySharedTasksAsync()).Data);
    }
}
