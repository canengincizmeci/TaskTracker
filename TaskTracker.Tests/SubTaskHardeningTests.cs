using Microsoft.EntityFrameworkCore;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;
using static TaskTracker.Tests.SubTaskWorkflowTests;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class SubTaskHardeningTests
{
    [Fact]
    public async Task Dashboard_applies_the_same_filters_and_pagination_to_parent_and_child_rows()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await VerifyDashboard(context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_dashboard_projects_subtask_only_assignees_and_owner_review_rows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyDashboard(context);
    }

    private static async Task VerifyDashboard(TaskTrackerDbContext context)
    {
        var workspaceId = await SeedWorkspaceAsync(context);
        context.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = 3 });
        await context.SaveChangesAsync();
        var service = TaskCollaborationTestServices.SubTasks(context);
        var first = await StartDelegatedAsync(service, 2, "Child A");
        var second = await StartDelegatedAsync(service, 3, "Child B");
        var parent = await context.TaskRequests.SingleAsync();
        var firstEntity = await context.SubTasks.SingleAsync(x => x.Id == first.Id);
        firstEntity.DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
        firstEntity.Priority = TaskPriority.High;
        await context.SaveChangesAsync();
        first = Data(await service.GetAsync(1, first.Id, 2));
        var viewer = TaskCollaborationTestServices.WorkDashboard(context, 2);
        var all = Data(await viewer.GetTasksAsync(new()));
        Assert.Equal(first.Id, Assert.Single(all.Items).SubTaskId);
        Assert.Equal("Workflow task", all.Items[0].ParentTitle);
        Assert.Equal(1, all.TotalCount);
        var assigned = Data(await viewer.GetTasksAsync(new()
        {
            Scope = WorkTaskScope.Assigned, Due = WorkTaskDueFilter.Overdue, Priority = TaskPriority.High, Search = "workflow task"
        }));
        Assert.Equal(first.Id, Assert.Single(assigned.Items).SubTaskId);
        Assert.Equal(1, Data(await viewer.GetSummaryAsync()).AssignedToMeCount);
        Assert.Equal(1, Data(await viewer.GetSummaryAsync()).OverdueCount);

        parent.AssigneeUserId = 2;
        parent.DueDate = firstEntity.DueDate;
        await context.SaveChangesAsync();
        var page1 = Data(await viewer.GetTasksAsync(new() { Scope = WorkTaskScope.Assigned, PageSize = 1 }));
        var page2 = Data(await viewer.GetTasksAsync(new() { Scope = WorkTaskScope.Assigned, PageSize = 1, Page = 2 }));
        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Null(Assert.Single(page1.Items).SubTaskId);
        Assert.Equal(first.Id, Assert.Single(page2.Items).SubTaskId);
        Assert.Equal(2, Data(await viewer.GetSummaryAsync()).AssignedToMeCount);
        Assert.Equal(2, Data(await viewer.GetSummaryAsync()).OverdueCount);

        first = Data(await service.SubmitAsync(1, first.Id, new() { Version = first.Version, Content = "First delivery" }, 2));
        var owner = TaskCollaborationTestServices.WorkDashboard(context, 1);
        Assert.Equal(1, Data(await owner.GetSummaryAsync()).AwaitingMyReviewCount);
        var reviewing = Assert.Single(Data(await owner.GetTasksAsync(new()
            { Scope = WorkTaskScope.Owned, Status = TaskStatus.InReview, Sort = WorkTaskSort.ReviewAge })).Items);
        Assert.Equal(first.Id, reviewing.SubTaskId);
        Assert.Equal(WorkTaskNextAction.Review, reviewing.NextAction);
        Assert.Equal(first.CurrentSubmissionId, reviewing.LatestSubmissionId);
        Assert.Equal(TaskStatus.InProgress, Data(await service.GetAsync(1, second.Id, 3)).Status);

        var membership = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 2);
        membership.IsActive = false;
        membership.RemovedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        Assert.Empty(Data(await viewer.GetTasksAsync(new())).Items);
        Assert.Equal(0, Data(await viewer.GetSummaryAsync()).AssignedToMeCount);
        Assert.Equal(0, Data(await owner.GetSummaryAsync()).AwaitingMyReviewCount);
    }

    [Fact]
    public async Task Creation_assigns_atomically_and_notifications_follow_committed_workflow_transitions()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context, TaskPermission.View);
        var recorded = new RecordingNotifications(context);
        var service = TaskCollaborationTestServices.SubTasks(context, notifications: recorded);
        Assert.IsAssignableFrom<IConflictResult>(await service.CreateAsync(1, Details() with { AssigneeUserId = 3 }, 1));
        Assert.Empty(await context.SubTasks.ToListAsync());
        Assert.Empty(await context.SubTaskActivities.ToListAsync());
        Assert.Empty(recorded.Deliveries);

        var item = Data(await service.CreateAsync(1, Details() with { AssigneeUserId = 2 }, 1));
        Assert.True(item.Capabilities!.CanAssign);
        Assert.Equal(2, item.AssigneeUserId);
        Assert.Equal("Subtask assigned", Assert.Single(recorded.Deliveries).Title);
        Assert.Collection(Data(await service.GetActivityHistoryAsync(1, item.Id, 1)),
            x => Assert.Equal(TaskActivityType.TaskCreated, x.ActivityType),
            x => Assert.Equal(TaskActivityType.UserAssigned, x.ActivityType));
        Assert.Equal(item.Capabilities, Assert.Single(Data(await service.ListAsync(1, 1))).Capabilities);
        Assert.True(Assert.Single(Data(await service.ListAsync(1, 2))).Capabilities!.CanStart);
        Assert.False(Assert.Single(Data(await service.ListAsync(1, 3))).Capabilities!.CanStart);
        item = Data(await service.AssignAsync(1, item.Id, new() { Version = item.Version, AssigneeUserId = 2 }, 1));
        Assert.Single(recorded.Deliveries);
        item = Data(await service.StartAsync(1, item.Id, Version(item.Version), 2));
        Assert.True(item.Capabilities!.CanSubmit);
        item = Data(await service.SubmitAsync(1, item.Id, new() { Version = item.Version, Content = "Draft" }, 2));
        Assert.False(item.Capabilities!.CanReview);
        Assert.True(Data(await service.GetAsync(1, item.Id, 1)).Capabilities!.CanReview);
        var submitted = item.CurrentSubmissionId!.Value;
        item = Data(await service.ReviewAsync(1, item.Id, submitted,
            Review(item.Version, TaskReviewDecision.ChangesRequested, "Revise"), 1));
        Assert.IsAssignableFrom<IConflictResult>(await service.ReviewAsync(1, item.Id, submitted, Review(item.Version), 1));
        item = Data(await service.SubmitAsync(1, item.Id, new() { Version = item.Version, Content = "Revision" }, 2));
        item = Data(await service.ReviewAsync(1, item.Id, item.CurrentSubmissionId!.Value, Review(item.Version), 1));
        Assert.True(item.Capabilities!.CanReopen);
        item = Data(await service.ReopenAsync(1, item.Id, Version(item.Version), 1));
        recorded.Fail = true;
        item = Data(await service.UnassignAsync(1, item.Id, Version(item.Version), 1));
        Assert.Null(item.AssigneeUserId);
        Assert.Null((await context.SubTasks.AsNoTracking().SingleAsync()).AssigneeUserId);
        Assert.Equal([2, 1, 2, 1, 2, 2], recorded.Deliveries.Select(x => x.UserId));
        Assert.Equal(["Subtask assigned", "Subtask awaiting review", "Subtask changes requested",
            "Subtask awaiting review", "Subtask approved", "Subtask unassigned"], recorded.Deliveries.Select(x => x.Title));
        Assert.All(recorded.Deliveries, entry => Assert.Equal($"/tasks/task-detail/1?subtask={item.Id}", entry.Url));

        recorded.Fail = false;
        var thirdShare = await context.TaskShares.SingleAsync(x => x.SharedWithUserId == 3);
        thirdShare.Permission = TaskPermission.Edit;
        await context.SaveChangesAsync();
        item = Data(await service.AssignAsync(1, item.Id, new() { Version = item.Version, AssigneeUserId = 2 }, 1));
        var deliveries = recorded.Deliveries.Count;
        item = Data(await service.AssignAsync(1, item.Id, new() { Version = item.Version, AssigneeUserId = 3 }, 1));
        Assert.Equal(3, item.AssigneeUserId);
        Assert.Equal([(2, "Subtask unassigned"), (3, "Subtask assigned")],
            recorded.Deliveries.Skip(deliveries).Select(x => (x.UserId, x.Title)));
    }

    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_history_read_finishes_before_concurrent_access_revocation_and_new_reads_are_denied(bool workspace)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        int subTaskId;
        var workspaceId = 0;
        await using (var seed = database.CreateContext())
        {
            if (workspace) workspaceId = await SeedWorkspaceAsync(seed);
            else await SeedPersonalAsync(seed);
            var service = TaskCollaborationTestServices.SubTasks(seed);
            var item = await StartDelegatedAsync(service, 2, "Evidence");
            item = Data(await service.SubmitAsync(1, item.Id, new() { Version = item.Version, Content = "Private evidence" }, 2));
            item = Data(await service.ReviewAsync(1, item.Id, item.CurrentSubmissionId!.Value, Review(item.Version), 1));
            subTaskId = item.Id;
        }
        var holdHistory = new PostgreSqlCommandGate("FROM \"SubTaskSubmissions\" AS", pause: true);
        var attemptingRevocation = new PostgreSqlCommandGate("FOR NO KEY UPDATE", pause: false);
        await using var reader = database.CreateContext(holdHistory);
        await using var remover = database.CreateContext(attemptingRevocation);
        var read = TaskCollaborationTestServices.SubTasks(reader).GetSubmissionHistoryAsync(1, subTaskId, 2);
        Task<IResult> remove;
        try
        {
            await holdHistory.Arrived;
            remove = workspace
                ? TaskCollaborationTestServices.Workspaces(remover, 1).RemoveMemberAsync(workspaceId, 2, new() { Version = 0 })
                : TaskCollaborationTestServices.TaskShares(remover, 1).RemoveParticipantAsync(1, 2, 0);
            await attemptingRevocation.Arrived;
            Assert.False(remove.IsCompleted);
        }
        finally { holdHistory.Release(); }
        Assert.Single(Data(await read.WaitAsync(TimeSpan.FromSeconds(20))));
        Assert.True((await remove.WaitAsync(TimeSpan.FromSeconds(20))).Success);
        Assert.False((await TaskCollaborationTestServices.SubTasks(reader).GetSubmissionHistoryAsync(1, subTaskId, 2)).Success);
    }

    private sealed class RecordingNotifications(TaskTrackerDbContext context) : INotificationService
    {
        public List<(int UserId, string Title, string? Url)> Deliveries { get; } = [];
        public bool Fail { get; set; }

        public async Task CreateTaskNotificationAsync(int userId, NotificationType type, string title, string message,
            int taskId, string? redirectUrl = null)
        {
            Assert.Null(context.Database.CurrentTransaction);
            Assert.True(await context.SubTasks.AsNoTracking().AnyAsync(x => x.TaskRequestId == taskId));
            Deliveries.Add((userId, title, redirectUrl));
            if (Fail) throw new IOException("Simulated notification delivery failure.");
        }

        public Task CreateTaskShareInvitationNotificationAsync(int userId, string taskTitle, string inviterUserName, int invitationId) =>
            throw new NotSupportedException();
        public Task<IDataResult<List<NotificationDto>>> GetNotificationsForUserAsync(int userId) => throw new NotSupportedException();
        public Task<IResult> MarkAsReadAsync(int notificationId) => throw new NotSupportedException();
        public Task<IResult> MarkAllAsReadAsync() => throw new NotSupportedException();
    }
}
