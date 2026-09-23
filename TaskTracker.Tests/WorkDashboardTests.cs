using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class WorkDashboardTests
{
    [Fact]
    public async Task Assigned_count_uses_only_active_current_responsibility()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var pending = TestDatabase.Task(1); pending.AssigneeUserId = 2;
        var inProgress = TestDatabase.Task(2); inProgress.AssigneeUserId = 2; inProgress.Status = TaskStatus.InProgress;
        var inReview = TestDatabase.Task(3); inReview.AssigneeUserId = 2; inReview.Status = TaskStatus.InReview;
        var completed = TestDatabase.Task(4); completed.AssigneeUserId = 2; completed.Status = TaskStatus.Completed;
        var cancelled = TestDatabase.Task(5); cancelled.AssigneeUserId = 2; cancelled.Status = TaskStatus.Cancelled;
        var inactive = TestDatabase.Task(6); inactive.AssigneeUserId = 2; inactive.Activity = false;
        var sharedOnly = TestDatabase.Task(7);
        var assignedElsewhere = TestDatabase.Task(8); assignedElsewhere.AssigneeUserId = 3;
        context.TaskRequests.AddRange(pending, inProgress, inReview, completed, cancelled, inactive, sharedOnly,
            assignedElsewhere);
        context.TaskShares.Add(new TaskShare
        {
            TaskRequestId = 7, SharedWithUserId = 2, Permission = TaskPermission.Manage
        });
        await context.SaveChangesAsync();

        var summary = (await WorkspaceTestServices.WorkDashboard(context, 2).GetSummaryAsync()).Data;

        Assert.Equal(3, summary.AssignedToMeCount);
    }

    [Fact]
    public async Task Awaiting_review_counts_only_latest_actionable_submission_for_owner()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var actionable = DelegatedTask(1, ownerId: 1);
        var anotherOwner = DelegatedTask(2, ownerId: 3);
        var alreadyReviewed = DelegatedTask(3, ownerId: 1);
        context.TaskRequests.AddRange(actionable, anotherOwner, alreadyReviewed);
        context.TaskShares.AddRange(EditShare(1), EditShare(2), EditShare(3));
        context.TaskSubmissions.AddRange(
            Submission(1, 1), Submission(2, 2), Submission(3, 3));
        await context.SaveChangesAsync();
        context.TaskSubmissionReviews.Add(new TaskSubmissionReview
        {
            TaskSubmissionId = 3, ReviewerUserId = 1, Decision = TaskReviewDecision.Approved
        });
        await context.SaveChangesAsync();

        var summary = (await WorkspaceTestServices.WorkDashboard(context, 1).GetSummaryAsync()).Data;

        Assert.Equal(1, summary.AwaitingMyReviewCount);
    }

    [Fact]
    public async Task Overdue_count_uses_utc_date_and_owned_or_assigned_nonterminal_tasks()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var owned = DueTask(1, today.AddDays(-1));
        var assigned = DueTask(2, today.AddDays(-3), ownerId: 3); assigned.AssigneeUserId = 1;
        var future = DueTask(3, today.AddDays(1));
        var noDueDate = TestDatabase.Task(4);
        var completed = DueTask(5, today.AddDays(-1)); completed.Status = TaskStatus.Completed;
        var cancelled = DueTask(6, today.AddDays(-1)); cancelled.Status = TaskStatus.Cancelled;
        var inactive = DueTask(7, today.AddDays(-1)); inactive.Activity = false;
        var sharedOnly = DueTask(8, today.AddDays(-1), ownerId: 3);
        context.TaskRequests.AddRange(owned, assigned, future, noDueDate, completed, cancelled, inactive, sharedOnly);
        context.TaskShares.Add(new TaskShare
        {
            TaskRequestId = 8, SharedWithUserId = 1, Permission = TaskPermission.Manage
        });
        await context.SaveChangesAsync();

        var summary = (await WorkspaceTestServices.WorkDashboard(context, 1).GetSummaryAsync()).Data;

        Assert.Equal(2, summary.OverdueCount);
    }

    [Fact]
    public async Task Pending_invitation_count_matches_actionable_invitation_semantics()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var active = TestDatabase.Task(1);
        var inactive = TestDatabase.Task(2); inactive.Activity = false;
        var completed = TestDatabase.Task(3); completed.Status = TaskStatus.Completed;
        context.TaskRequests.AddRange(active, inactive, completed);
        var future = DateTime.UtcNow.AddDays(1);
        context.TaskShareInvitations.AddRange(
            Invitation(1, 1, 2, TaskShareInvitationStatus.Pending, future),
            Invitation(2, 1, 2, TaskShareInvitationStatus.Accepted, future),
            Invitation(3, 1, 2, TaskShareInvitationStatus.Rejected, future),
            Invitation(4, 1, 2, TaskShareInvitationStatus.Cancelled, future),
            Invitation(5, 1, 3, TaskShareInvitationStatus.Pending, future),
            Invitation(6, 1, 2, TaskShareInvitationStatus.Pending, DateTime.UtcNow.AddDays(-1)),
            Invitation(7, 2, 2, TaskShareInvitationStatus.Pending, future),
            Invitation(8, 3, 2, TaskShareInvitationStatus.Pending, future));
        await context.SaveChangesAsync();

        var summary = (await WorkspaceTestServices.WorkDashboard(context, 2).GetSummaryAsync()).Data;

        Assert.Equal(1, summary.PendingInvitationCount);
    }

    [Fact]
    public async Task Summary_combines_all_counts_for_authenticated_user()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdueOwned = DueTask(1, today.AddDays(-1));
        var assigned = TestDatabase.Task(2); assigned.OwnerId = 3; assigned.AssigneeUserId = 1;
        var awaitingReview = DelegatedTask(3, ownerId: 1);
        context.TaskRequests.AddRange(overdueOwned, assigned, awaitingReview);
        context.TaskShares.Add(EditShare(3));
        context.TaskSubmissions.Add(Submission(1, 3));
        context.TaskShareInvitations.Add(Invitation(
            1, 1, 1, TaskShareInvitationStatus.Pending, DateTime.UtcNow.AddDays(1)));
        await context.SaveChangesAsync();

        var summary = (await WorkspaceTestServices.WorkDashboard(context, 1).GetSummaryAsync()).Data;

        Assert.Equal(1, summary.AssignedToMeCount);
        Assert.Equal(1, summary.AwaitingMyReviewCount);
        Assert.Equal(1, summary.OverdueCount);
        Assert.Equal(1, summary.PendingInvitationCount);
    }

    private static TaskRequest DelegatedTask(int id, int ownerId)
    {
        var task = TestDatabase.Task(id);
        task.OwnerId = ownerId;
        task.AssigneeUserId = 2;
        task.Status = TaskStatus.InReview;
        return task;
    }

    private static TaskRequest DueTask(int id, DateOnly dueDate, int ownerId = 1)
    {
        var task = TestDatabase.Task(id);
        task.OwnerId = ownerId;
        task.DueDate = dueDate;
        return task;
    }

    private static TaskShare EditShare(int taskId) => new()
    {
        TaskRequestId = taskId, SharedWithUserId = 2, Permission = TaskPermission.Edit
    };

    private static TaskSubmission Submission(int id, int taskId) => new()
    {
        Id = id, TaskRequestId = taskId, SubmittedByUserId = 2,
        RevisionNumber = 1, Content = $"Submission {id}"
    };

    private static TaskShareInvitation Invitation(int id, int taskId, int invitedUserId,
        TaskShareInvitationStatus status, DateTime expiresAt) => new()
    {
        Id = id, TaskRequestId = taskId, InvitedByUserId = 1, InvitedUserId = invitedUserId,
        Permission = TaskPermission.View, Status = status, ExpiresAt = expiresAt
    };
}
