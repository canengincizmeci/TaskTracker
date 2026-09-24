using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;
using TaskTracker.Bussiness.Abstract;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class SubmissionReviewTests
{
    private sealed class FailingNotifications : INotificationService
    {
        public Task CreateTaskShareInvitationNotificationAsync(int userId, string taskTitle, string inviterUserName, int invitationId) =>
            Task.FromException(new IOException("Unavailable"));
        public Task CreateTaskNotificationAsync(int userId, NotificationType type, string title, string message,
            int taskId, string? redirectUrl = null) => Task.FromException(new IOException("Unavailable"));
        public Task<IDataResult<List<NotificationDto>>> GetNotificationsForUserAsync(int userId) => throw new NotSupportedException();
        public Task<IResult> MarkAsReadAsync(int notificationId) => throw new NotSupportedException();
        public Task<IResult> MarkAllAsReadAsync() => throw new NotSupportedException();
    }
    private static async Task SeedDelegated(TaskTracker.Core.DataAccess.TaskTrackerDbContext context,
        TaskStatus status = TaskStatus.InProgress)
    {
        var task = TestDatabase.Task();
        task.AssigneeUserId = 2;
        task.Status = status;
        context.TaskRequests.Add(task);
        context.TaskShares.AddRange(
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit },
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 3, Permission = TaskPermission.Manage });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Current_delegated_assignee_submits_atomically()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);

        var result = await WorkspaceTestServices.TaskRequests(context).SubmitAsync(1,
            new() { Version = 0, Content = "  Finished work  " }, 2);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data.RevisionNumber);
        Assert.Equal("Finished work", result.Data.Content);
        Assert.Equal(TaskStatus.InReview, (await context.TaskRequests.SingleAsync()).Status);
        var submission = await context.TaskSubmissions.SingleAsync();
        var activity = await context.TaskActivities.SingleAsync();
        Assert.Equal(submission.Id, activity.SubmissionId);
        Assert.Equal((TaskActivityType.SubmissionCreated, TaskStatus.InProgress, TaskStatus.InReview),
            (activity.ActivityType, activity.FromStatus, activity.ToStatus));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Owner_and_non_assignee_cannot_submit(int actorId)
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);
        var result = await WorkspaceTestServices.TaskRequests(context).SubmitAsync(1,
            new() { Version = 0, Content = "Work" }, actorId);
        Assert.False(result.Success);
        Assert.Empty(await context.TaskSubmissions.ToListAsync());
        Assert.Equal(TaskStatus.InProgress, (await context.TaskRequests.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(TaskPermission.View)]
    [InlineData(TaskPermission.Edit)]
    [InlineData(TaskPermission.Manage)]
    public async Task Participant_permission_never_replaces_current_responsibility(TaskPermission permission)
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);
        (await context.TaskShares.SingleAsync(x => x.SharedWithUserId == 3)).Permission = permission;
        await context.SaveChangesAsync();
        var result = await WorkspaceTestServices.TaskRequests(context).SubmitAsync(1,
            new() { Version = 0, Content = "Not my assignment" }, 3);
        Assert.False(result.Success);
        Assert.Empty(await context.TaskSubmissions.ToListAsync());
    }

    [Fact]
    public async Task Malformed_or_removed_assignee_cannot_start_or_submit()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        var task = TestDatabase.Task(); task.AssigneeUserId = 2;
        context.TaskRequests.Add(task);
        await context.SaveChangesAsync();
        var manager = WorkspaceTestServices.TaskRequests(context);
        Assert.False((await manager.StartTaskAsync(1, new() { Version = 0 }, 2)).Success);
        task.Status = TaskStatus.InProgress;
        await context.SaveChangesAsync();
        Assert.False((await manager.SubmitAsync(1, new() { Version = 1, Content = "Work" }, 2)).Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_submission_is_rejected(string content)
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);
        Assert.False((await WorkspaceTestServices.TaskRequests(context).SubmitAsync(1,
            new() { Version = 0, Content = content }, 2)).Success);
        Assert.Empty(await context.TaskSubmissions.ToListAsync());
    }

    [Fact]
    public async Task Stale_or_wrong_state_submission_returns_conflict()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);
        var manager = WorkspaceTestServices.TaskRequests(context);
        var stale = await manager.SubmitAsync(1, new() { Version = 4, Content = "Work" }, 2);
        Assert.IsAssignableFrom<IConflictResult>(stale);
        (await context.TaskRequests.SingleAsync()).Status = TaskStatus.Pending;
        await context.SaveChangesAsync();
        var wrongState = await manager.SubmitAsync(1, new() { Version = 1, Content = "Work" }, 2);
        Assert.IsAssignableFrom<IConflictResult>(wrongState);
    }

    [Fact]
    public async Task Changes_requested_then_resubmission_then_approval_preserves_history()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);
        var manager = WorkspaceTestServices.TaskRequests(context);
        var first = await manager.SubmitAsync(1, new() { Version = 0, Content = "Revision one" }, 2);
        var changes = await manager.ReviewAsync(1, first.Data.Id,
            new() { Version = 1, Decision = TaskReviewDecision.ChangesRequested, Feedback = "Add evidence" }, 1);
        Assert.True(changes.Success);
        Assert.Equal(TaskStatus.InProgress, (await context.TaskRequests.SingleAsync()).Status);

        var second = await manager.SubmitAsync(1, new() { Version = 2, Content = "Revision two" }, 2);
        Assert.Equal(2, second.Data.RevisionNumber);
        var approval = await manager.ReviewAsync(1, second.Data.Id,
            new() { Version = 3, Decision = TaskReviewDecision.Approved }, 1);

        Assert.True(approval.Success);
        Assert.Equal(TaskStatus.Completed, (await context.TaskRequests.SingleAsync()).Status);
        var history = (await manager.GetSubmissionHistoryAsync(1, 2)).Data;
        Assert.Collection(history,
            revision => { Assert.Equal("Revision one", revision.Content); Assert.Equal("ChangesRequested", revision.Review!.Decision); },
            revision => { Assert.Equal("Revision two", revision.Content); Assert.Equal("Approved", revision.Review!.Decision); });
    }

    [Fact]
    public async Task Only_owner_can_review_and_changes_require_feedback()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);
        var manager = WorkspaceTestServices.TaskRequests(context);
        var submission = await manager.SubmitAsync(1, new() { Version = 0, Content = "Work" }, 2);
        Assert.False((await manager.ReviewAsync(1, submission.Data.Id,
            new() { Version = 1, Decision = TaskReviewDecision.Approved }, 3)).Success);
        Assert.False((await manager.ReviewAsync(1, submission.Data.Id,
            new() { Version = 1, Decision = TaskReviewDecision.ChangesRequested, Feedback = " " }, 1)).Success);
        Assert.Empty(await context.TaskSubmissionReviews.ToListAsync());
    }

    [Fact]
    public async Task Old_and_duplicate_review_are_conflicts()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);
        var manager = WorkspaceTestServices.TaskRequests(context);
        var first = await manager.SubmitAsync(1, new() { Version = 0, Content = "One" }, 2);
        await manager.ReviewAsync(1, first.Data.Id,
            new() { Version = 1, Decision = TaskReviewDecision.ChangesRequested, Feedback = "Again" }, 1);
        var second = await manager.SubmitAsync(1, new() { Version = 2, Content = "Two" }, 2);
        var old = await manager.ReviewAsync(1, first.Data.Id,
            new() { Version = 3, Decision = TaskReviewDecision.Approved }, 1);
        Assert.IsAssignableFrom<IConflictResult>(old);
        await manager.ReviewAsync(1, second.Data.Id,
            new() { Version = 3, Decision = TaskReviewDecision.Approved }, 1);
        var duplicate = await manager.ReviewAsync(1, second.Data.Id,
            new() { Version = 4, Decision = TaskReviewDecision.Approved }, 1);
        Assert.IsAssignableFrom<IConflictResult>(duplicate);
        Assert.Equal(2, await context.TaskSubmissionReviews.CountAsync());
    }

    [Fact]
    public async Task Cancel_in_review_and_reopen_preserve_non_actionable_history()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);
        var manager = WorkspaceTestServices.TaskRequests(context);
        var submission = await manager.SubmitAsync(1, new() { Version = 0, Content = "Keep me" }, 2);
        Assert.True((await manager.CancelTaskAsync(1, new() { Version = 1 }, 1)).Success);
        Assert.Empty(await context.TaskSubmissionReviews.ToListAsync());
        var staleReview = await manager.ReviewAsync(1, submission.Data.Id,
            new() { Version = 2, Decision = TaskReviewDecision.Approved }, 1);
        Assert.IsAssignableFrom<IConflictResult>(staleReview);
        Assert.True((await manager.ReopenTaskAsync(1, new() { Version = 2 }, 1)).Success);
        Assert.Equal("Keep me", Assert.Single((await manager.GetSubmissionHistoryAsync(1, 1)).Data).Content);
    }

    [Fact]
    public async Task Reopened_approval_is_historical_and_next_delivery_uses_next_revision()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);
        var manager = WorkspaceTestServices.TaskRequests(context);
        var first = await manager.SubmitAsync(1, new() { Version = 0, Content = "Approved once" }, 2);
        await manager.ReviewAsync(1, first.Data.Id,
            new() { Version = 1, Decision = TaskReviewDecision.Approved }, 1);
        Assert.True((await manager.ReopenTaskAsync(1, new() { Version = 2 }, 1)).Success);
        Assert.True((await manager.StartTaskAsync(1, new() { Version = 3 }, 2)).Success);
        var second = await manager.SubmitAsync(1, new() { Version = 4, Content = "New cycle" }, 2);
        Assert.Equal(2, second.Data.RevisionNumber);
        Assert.Equal("Approved", (await manager.GetSubmissionHistoryAsync(1, 1)).Data[0].Review!.Decision);
    }

    [Fact]
    public async Task Workspace_access_controls_history_and_orphan_review_queue_is_visible()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context, TaskStatus.InReview);
        var manager = WorkspaceTestServices.TaskRequests(context);
        Assert.True((await manager.GetSubmissionHistoryAsync(1, 2)).Success);
        Assert.False((await manager.GetSubmissionHistoryAsync(1, 99)).Success);
        var queued = Assert.Single((await manager.GetAwaitingReviewAsync(1)).Data);
        Assert.Null(queued.LatestSubmissionId);
        Assert.False((await manager.ReviewAsync(1, 42,
            new() { Version = 0, Decision = TaskReviewDecision.Approved }, 1)).Success);
    }

    [Fact]
    public async Task Notification_and_realtime_failures_do_not_undo_submission()
    {
        using var db = new TestDatabase(); using var context = db.CreateContext();
        await SeedDelegated(context);
        var manager = WorkspaceTestServices.TaskRequests(context, new FailingNotifications(),
            WorkspaceTestServices.Create(context, failRealtime: true));

        var result = await manager.SubmitAsync(1, new() { Version = 0, Content = "Durable" }, 2);

        Assert.True(result.Success);
        using var verify = db.CreateContext();
        Assert.Single(await verify.TaskSubmissions.ToListAsync());
        Assert.Equal(TaskStatus.InReview, (await verify.TaskRequests.SingleAsync()).Status);
    }
}
