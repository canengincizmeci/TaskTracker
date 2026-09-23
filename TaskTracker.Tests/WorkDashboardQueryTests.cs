using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.API.Controllers;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class WorkDashboardQueryTests
{
    [Fact]
    public async Task Scopes_return_authorized_active_tasks_once()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var owned = Task(1, 1);
        var assigned = Task(2, 2); assigned.AssigneeUserId = 1;
        var shared = Task(3, 2);
        var multiRelationship = Task(4, 1); multiRelationship.AssigneeUserId = 1;
        var unrelated = Task(5, 2);
        var inactive = Task(6, 1); inactive.Activity = false;
        context.TaskRequests.AddRange(owned, assigned, shared, multiRelationship, unrelated, inactive);
        context.TaskShares.AddRange(Share(3, 1), Share(4, 1));
        await context.SaveChangesAsync();

        var service = WorkspaceTestServices.WorkDashboard(context, 1);
        var all = (await service.GetTasksAsync(new() { Sort = WorkTaskSort.Recent })).Data;
        var ownedPage = (await service.GetTasksAsync(new() { Scope = WorkTaskScope.Owned })).Data;
        var assignedPage = (await service.GetTasksAsync(new() { Scope = WorkTaskScope.Assigned })).Data;
        var sharedPage = (await service.GetTasksAsync(new() { Scope = WorkTaskScope.Shared })).Data;

        Assert.Equal([1, 2, 3, 4], all.Items.Select(x => x.TaskId).Order());
        Assert.Equal([1, 4], ownedPage.Items.Select(x => x.TaskId).Order());
        Assert.Equal([2, 4], assignedPage.Items.Select(x => x.TaskId).Order());
        Assert.Equal([3, 4], sharedPage.Items.Select(x => x.TaskId).Order());
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(4, all.Items.Select(x => x.TaskId).Distinct().Count());
        var multi = all.Items.Single(x => x.TaskId == 4);
        Assert.True(multi.IsOwned && multi.IsAssigned && multi.IsShared);
    }

    [Fact]
    public async Task Search_matches_title_description_and_category_case_insensitively()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var title = Task(1, 1); title.Title = "Quarterly REPORT";
        var description = Task(2, 1); description.Description = "Prepare release Notes";
        var category = Task(3, 1); category.Category = "CUSTOMER Success";
        var noMatch = Task(4, 1);
        context.TaskRequests.AddRange(title, description, category, noMatch);
        await context.SaveChangesAsync();
        var service = WorkspaceTestServices.WorkDashboard(context, 1);

        Assert.Equal(1, (await service.GetTasksAsync(new() { Search = "report" })).Data.Items.Single().TaskId);
        Assert.Equal(2, (await service.GetTasksAsync(new() { Search = "RELEASE notes" })).Data.Items.Single().TaskId);
        Assert.Equal(3, (await service.GetTasksAsync(new() { Search = "customer SUCCESS" })).Data.Items.Single().TaskId);
        Assert.Empty((await service.GetTasksAsync(new() { Search = "missing" })).Data.Items);
    }

    [Fact]
    public async Task Status_and_priority_filters_use_domain_values()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var pendingCritical = Task(1, 1); pendingCritical.Priority = TaskPriority.Critical;
        var progressCritical = Task(2, 1); progressCritical.Status = TaskStatus.InProgress;
        progressCritical.Priority = TaskPriority.Critical;
        var pendingLow = Task(3, 1); pendingLow.Priority = TaskPriority.Low;
        context.TaskRequests.AddRange(pendingCritical, progressCritical, pendingLow);
        await context.SaveChangesAsync();

        var page = (await WorkspaceTestServices.WorkDashboard(context, 1).GetTasksAsync(new()
        {
            Status = TaskStatus.Pending,
            Priority = TaskPriority.Critical
        })).Data;

        Assert.Equal(1, page.Items.Single().TaskId);
    }

    [Fact]
    public async Task Due_filters_use_utc_date_and_overdue_excludes_terminal_tasks()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdue = Task(1, 1); overdue.DueDate = today.AddDays(-1);
        var completed = Task(2, 1); completed.DueDate = today.AddDays(-1); completed.Status = TaskStatus.Completed;
        var cancelled = Task(3, 1); cancelled.DueDate = today.AddDays(-1); cancelled.Status = TaskStatus.Cancelled;
        var dueToday = Task(4, 1); dueToday.DueDate = today;
        var soon = Task(5, 1); soon.DueDate = today.AddDays(7);
        var later = Task(6, 1); later.DueDate = today.AddDays(8);
        var noDueDate = Task(7, 1);
        context.TaskRequests.AddRange(overdue, completed, cancelled, dueToday, soon, later, noDueDate);
        await context.SaveChangesAsync();
        var service = WorkspaceTestServices.WorkDashboard(context, 1);

        Assert.Equal(new[] { 1 }, await Ids(service, WorkTaskDueFilter.Overdue));
        Assert.Equal(new[] { 4 }, await Ids(service, WorkTaskDueFilter.Today));
        Assert.Equal(new[] { 5 }, await Ids(service, WorkTaskDueFilter.Soon));
        Assert.Equal(new[] { 7 }, await Ids(service, WorkTaskDueFilter.None));
    }

    [Fact]
    public async Task Pagination_returns_metadata_and_stable_pages()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        context.TaskRequests.AddRange(Enumerable.Range(1, 5).Select(id => Task(id, 1)));
        await context.SaveChangesAsync();
        var service = WorkspaceTestServices.WorkDashboard(context, 1);

        var first = (await service.GetTasksAsync(new() { Page = 1, PageSize = 2 })).Data;
        var second = (await service.GetTasksAsync(new() { Page = 2, PageSize = 2 })).Data;

        Assert.Equal([1, 2], first.Items.Select(x => x.TaskId));
        Assert.Equal([3, 4], second.Items.Select(x => x.TaskId));
        Assert.Equal(5, second.TotalCount);
        Assert.Equal(3, second.TotalPages);
        Assert.Equal(2, second.Page);
        Assert.Equal(2, second.PageSize);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_pagination_is_rejected(int page, int pageSize)
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();

        var result = await WorkspaceTestServices.WorkDashboard(context, 1)
            .GetTasksAsync(new() { Page = page, PageSize = pageSize });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Undefined_filter_values_are_rejected()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var service = WorkspaceTestServices.WorkDashboard(context, 1);

        Assert.False((await service.GetTasksAsync(new() { Scope = (WorkTaskScope)999 })).Success);
        Assert.False((await service.GetTasksAsync(new() { Due = (WorkTaskDueFilter)999 })).Success);
        Assert.False((await service.GetTasksAsync(new() { Sort = (WorkTaskSort)999 })).Success);
        Assert.False((await service.GetTasksAsync(new() { Status = (TaskStatus)999 })).Success);
        Assert.False((await service.GetTasksAsync(new() { Priority = (TaskPriority)999 })).Success);
    }

    [Fact]
    public async Task Due_priority_and_recent_sorts_are_deterministic()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = Task(1, 1); first.DueDate = today.AddDays(2); first.Priority = TaskPriority.Low;
        first.CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var second = Task(2, 1); second.Priority = TaskPriority.Critical;
        second.CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var third = Task(3, 1); third.DueDate = today.AddDays(1); third.Priority = TaskPriority.High;
        third.CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        context.TaskRequests.AddRange(first, second, third);
        await context.SaveChangesAsync();
        var service = WorkspaceTestServices.WorkDashboard(context, 1);

        Assert.Equal([3, 1, 2], (await service.GetTasksAsync(new() { Sort = WorkTaskSort.Due })).Data.Items.Select(x => x.TaskId));
        Assert.Equal([2, 3, 1], (await service.GetTasksAsync(new() { Sort = WorkTaskSort.Priority })).Data.Items.Select(x => x.TaskId));
        Assert.Equal([3, 2, 1], (await service.GetTasksAsync(new() { Sort = WorkTaskSort.Recent })).Data.Items.Select(x => x.TaskId));
    }

    [Fact]
    public async Task Review_age_puts_oldest_actionable_submission_first()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var oldReview = ReviewTask(1);
        var newReview = ReviewTask(2);
        var ordinary = Task(3, 1);
        context.TaskRequests.AddRange(oldReview, newReview, ordinary);
        context.TaskShares.AddRange(Share(1, 2, TaskPermission.Edit), Share(2, 2, TaskPermission.Edit));
        context.TaskSubmissions.AddRange(
            Submission(1, 1, 2, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Submission(2, 2, 2, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
        await context.SaveChangesAsync();

        var page = (await WorkspaceTestServices.WorkDashboard(context, 1)
            .GetTasksAsync(new() { Sort = WorkTaskSort.ReviewAge })).Data;

        Assert.Equal([1, 2, 3], page.Items.Select(x => x.TaskId));
        Assert.NotNull(page.Items[0].ReviewSubmittedAt);
        Assert.Null(page.Items[2].ReviewSubmittedAt);
    }

    [Fact]
    public async Task Next_action_matches_current_workflow_capabilities()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        var start = Task(1, 2); start.AssigneeUserId = 1;
        var submit = Task(2, 2); submit.AssigneeUserId = 1; submit.Status = TaskStatus.InProgress;
        var revise = Task(3, 2); revise.AssigneeUserId = 1; revise.Status = TaskStatus.InProgress;
        var review = ReviewTask(4);
        var view = Task(5, 1); view.AssigneeUserId = 2;
        context.TaskRequests.AddRange(start, submit, revise, review, view);
        context.TaskShares.AddRange(Share(1, 1, TaskPermission.Edit), Share(2, 1, TaskPermission.Edit),
            Share(3, 1, TaskPermission.Edit),
            Share(4, 2, TaskPermission.Edit));
        context.TaskSubmissions.AddRange(Submission(1, 3, 1, DateTime.UtcNow.AddDays(-2)),
            Submission(2, 4, 2, DateTime.UtcNow.AddDays(-1)));
        await context.SaveChangesAsync();
        context.TaskSubmissionReviews.Add(new TaskSubmissionReview
        {
            TaskSubmissionId = 1,
            ReviewerUserId = 2,
            Decision = TaskReviewDecision.ChangesRequested,
            Feedback = "Please revise"
        });
        await context.SaveChangesAsync();

        var items = (await WorkspaceTestServices.WorkDashboard(context, 1)
            .GetTasksAsync(new() { Sort = WorkTaskSort.Due })).Data.Items.ToDictionary(x => x.TaskId);

        Assert.Equal(WorkTaskNextAction.Start, items[1].NextAction);
        Assert.Equal(WorkTaskNextAction.Submit, items[2].NextAction);
        Assert.Equal(WorkTaskNextAction.Revise, items[3].NextAction);
        Assert.Equal(WorkTaskNextAction.Review, items[4].NextAction);
        Assert.Equal(WorkTaskNextAction.View, items[5].NextAction);
    }

    [Fact]
    public async Task Authorized_tasks_endpoint_returns_paged_response_from_bound_query()
    {
        using var db = new TestDatabase();
        using var context = db.CreateContext();
        context.TaskRequests.Add(Task(1, 1));
        await context.SaveChangesAsync();
        var controller = new WorkDashboardController(WorkspaceTestServices.WorkDashboard(context, 1));

        var result = await controller.Tasks(new WorkTaskQueryDto { Scope = WorkTaskScope.Owned, PageSize = 10 });

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<PagedWorkTasksDto>(ok.Value);
        Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.NotNull(typeof(WorkDashboardController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Single());
        var action = typeof(WorkDashboardController).GetMethod(nameof(WorkDashboardController.Tasks))!;
        Assert.Equal("tasks", action.GetCustomAttributes(typeof(HttpGetAttribute), true)
            .Cast<HttpGetAttribute>().Single().Template);
        Assert.NotNull(action.GetParameters().Single().GetCustomAttributes(typeof(FromQueryAttribute), true).Single());
        Assert.Null(typeof(WorkTaskQueryDto).GetProperty("UserId"));
    }

    private static async Task<int[]> Ids(TaskTracker.Bussiness.Concrete.WorkDashboardManager service,
        WorkTaskDueFilter due) => (await service.GetTasksAsync(new() { Due = due })).Data.Items
        .Select(x => x.TaskId).ToArray();

    private static TaskRequest Task(int id, int ownerId) => new()
    {
        Id = id,
        OwnerId = ownerId,
        Title = $"Task {id}",
        Description = $"Description {id}",
        Category = "General",
        Activity = true,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, id, DateTimeKind.Utc)
    };

    private static TaskRequest ReviewTask(int id)
    {
        var task = Task(id, 1);
        task.AssigneeUserId = 2;
        task.Status = TaskStatus.InReview;
        return task;
    }

    private static TaskShare Share(int taskId, int userId, TaskPermission permission = TaskPermission.View) => new()
    {
        TaskRequestId = taskId,
        SharedWithUserId = userId,
        Permission = permission
    };

    private static TaskSubmission Submission(int id, int taskId, int userId, DateTime createdAt) => new()
    {
        Id = id,
        TaskRequestId = taskId,
        SubmittedByUserId = userId,
        RevisionNumber = 1,
        Content = $"Submission {id}",
        CreatedAt = createdAt
    };
}
