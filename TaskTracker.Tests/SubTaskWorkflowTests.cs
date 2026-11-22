using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskTracker.API.Controllers;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class SubTaskWorkflowTests
{
    internal static CreateSubTaskDto Details(string title = "Separate work") =>
        new() { Title = title, Description = "Subtask description" };

    internal static SubTaskCommandDto Version(long version) => new() { Version = version };

    internal static ReviewSubTaskSubmissionDto Review(long version, TaskReviewDecision decision = TaskReviewDecision.Approved,
        string? feedback = null) => new() { Version = version, Decision = decision, Feedback = feedback };

    internal static T Data<T>(IDataResult<T> result)
    {
        Assert.True(result.Success, result.Message);
        return result.Data;
    }

    internal static async Task SeedPersonalAsync(TaskTrackerDbContext context, TaskPermission? thirdPermission = TaskPermission.Edit)
    {
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskShares.Add(new TaskShare { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        if (thirdPermission.HasValue)
            context.TaskShares.Add(new TaskShare { TaskRequestId = 1, SharedWithUserId = 3, Permission = thirdPermission.Value });
        await context.SaveChangesAsync();
    }

    internal static async Task<int> SeedWorkspaceAsync(TaskTrackerDbContext context)
    {
        var workspace = new Workspace
        {
            Name = "Subtask workspace",
            Members =
            [
                new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner },
                new WorkspaceMember { UserId = 2, Role = WorkspaceRole.Member }
            ]
        };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();
        var task = TestDatabase.Task();
        task.WorkspaceId = workspace.Id;
        context.TaskRequests.Add(task);
        await context.SaveChangesAsync();
        return workspace.Id;
    }

    internal static async Task<SubTaskDto> StartDelegatedAsync(ISubTaskService service, int userId, string title)
    {
        var subTask = Data(await service.CreateAsync(1, Details(title), 1));
        subTask = Data(await service.AssignAsync(1, subTask.Id, new()
            { Version = subTask.Version, AssigneeUserId = userId }, 1));
        return Data(await service.StartAsync(1, subTask.Id, Version(subTask.Version), userId));
    }

    [Fact]
    public async Task Siblings_run_independently_with_their_own_assignees_revisions_and_reviews()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var first = await StartDelegatedAsync(service, 2, "First sibling");
        var second = await StartDelegatedAsync(service, 3, "Second sibling");

        Assert.Equal(Messages.AuthorizationDenied,
            (await service.SubmitAsync(1, first.Id, new() { Version = first.Version, Content = "Wrong actor" }, 3)).Message);
        Assert.IsAssignableFrom<IConflictResult>(await service.CompleteAsync(1, first.Id, Version(first.Version), 1));
        first = Data(await service.SubmitAsync(1, first.Id, new() { Version = first.Version, Content = " First revision " }, 2));
        second = Data(await service.SubmitAsync(1, second.Id, new() { Version = second.Version, Content = "Sibling work" }, 3));
        var originalId = first.CurrentSubmissionId!.Value;
        Assert.IsAssignableFrom<IConflictResult>(
            await service.ReviewAsync(1, first.Id, second.CurrentSubmissionId!.Value, Review(first.Version), 1));
        Assert.False((await service.ReviewAsync(1, first.Id, originalId,
            Review(first.Version, TaskReviewDecision.ChangesRequested, " \t "), 1)).Success);
        Assert.Equal(Messages.AuthorizationDenied, (await service.ReviewAsync(1, first.Id, originalId,
            Review(first.Version), 2)).Message);
        Assert.IsAssignableFrom<IConflictResult>(await service.AssignAsync(1, first.Id, new()
            { Version = first.Version, AssigneeUserId = 3 }, 1));
        Assert.IsAssignableFrom<IConflictResult>(await service.UnassignAsync(1, first.Id, Version(first.Version), 1));
        Assert.IsAssignableFrom<IConflictResult>(await service.UpdateAsync(1, first.Id, new()
            { Version = first.Version, Title = "New title", Description = "New details" }, 1));

        first = Data(await service.ReviewAsync(1, first.Id, originalId,
            Review(first.Version, TaskReviewDecision.ChangesRequested, " Clarify the result "), 1));
        Assert.Equal(TaskStatus.InProgress, first.Status);
        Assert.Null(first.CurrentSubmissionId);
        Assert.IsAssignableFrom<IConflictResult>(await service.ReviewAsync(1, first.Id, originalId, Review(first.Version), 1));
        first = Data(await service.SubmitAsync(1, first.Id, new() { Version = first.Version, Content = "Revised work" }, 2));
        Assert.NotEqual(originalId, first.CurrentSubmissionId);
        Assert.IsAssignableFrom<IConflictResult>(await service.ReviewAsync(1, first.Id, originalId, Review(first.Version), 1));
        first = Data(await service.ReviewAsync(1, first.Id, first.CurrentSubmissionId!.Value, Review(first.Version), 1));
        Assert.Equal(TaskStatus.Completed, first.Status);
        Assert.Equal(TaskStatus.InReview, Data(await service.GetAsync(1, second.Id, 2)).Status);

        var history = Data(await service.GetSubmissionHistoryAsync(1, first.Id, 3));
        Assert.Equal([2, 1], history.Select(x => x.RevisionNumber));
        Assert.Equal("First revision", history[1].Content);
        Assert.Equal("Clarify the result", history[1].Review!.Feedback);
        Assert.Equal(TaskReviewDecision.Approved, history[0].Review!.Decision);
        Assert.Equal(1, Assert.Single(Data(await service.GetSubmissionHistoryAsync(1, second.Id, 1))).RevisionNumber);
        Assert.Equal(0, (await context.TaskRequests.SingleAsync()).Version);
        Assert.Empty(await context.TaskSubmissions.ToListAsync());
        Assert.Equal([first.Id, second.Id], Data(await service.ListAsync(1, 2)).Select(x => x.Id));
    }

    [Fact]
    public async Task Owner_manages_and_completes_own_work_and_pending_or_view_only_users_are_ineligible()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context, TaskPermission.View);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var subTask = Data(await service.CreateAsync(1, Details(), 1));
        var command = Version(subTask.Version);
        var edit = new UpdateSubTaskDto { Version = subTask.Version, Title = "New title", Description = "Updated details" };
        var assignment = new AssignSubTaskDto { Version = subTask.Version, AssigneeUserId = 2 };

        IResult[] forbidden =
        [
            await service.CreateAsync(1, Details(), 2),
            await service.UpdateAsync(1, subTask.Id, edit, 2),
            await service.AssignAsync(1, subTask.Id, assignment, 2),
            await service.UnassignAsync(1, subTask.Id, command, 2),
            await service.StartAsync(1, subTask.Id, command, 2),
            await service.CompleteAsync(1, subTask.Id, command, 2),
            await service.CancelAsync(1, subTask.Id, command, 2),
            await service.ReopenAsync(1, subTask.Id, command, 2),
            await service.DeleteAsync(1, subTask.Id, command, 2)
        ];
        Assert.All(forbidden, result => Assert.Equal(Messages.AuthorizationDenied, result.Message));
        Assert.IsAssignableFrom<IConflictResult>(await service.AssignAsync(1, subTask.Id, assignment with { AssigneeUserId = 3 }, 1));
        context.TaskShares.Remove(await context.TaskShares.SingleAsync(x => x.SharedWithUserId == 3));
        context.TaskShareInvitations.Add(new TaskShareInvitation
        {
            TaskRequestId = 1, InvitedByUserId = 1, InvitedUserId = 3,
            Permission = TaskPermission.Edit, Status = TaskShareInvitationStatus.Pending
        });
        await context.SaveChangesAsync();
        Assert.IsAssignableFrom<IConflictResult>(await service.AssignAsync(1, subTask.Id, assignment with { AssigneeUserId = 3 }, 1));

        subTask = Data(await service.UpdateAsync(1, subTask.Id, edit, 1));
        Assert.Equal("New title", subTask.Title);
        Assert.IsAssignableFrom<IConflictResult>(await service.StartAsync(1, subTask.Id, command, 1));
        subTask = Data(await service.StartAsync(1, subTask.Id, Version(subTask.Version), 1));
        subTask = Data(await service.CompleteAsync(1, subTask.Id, Version(subTask.Version), 1));
        subTask = Data(await service.ReopenAsync(1, subTask.Id, Version(subTask.Version), 1));
        subTask = Data(await service.AssignAsync(1, subTask.Id, new() { Version = subTask.Version, AssigneeUserId = 1 }, 1));
        Assert.Equal(TaskStatus.Completed, Data(await service.CompleteAsync(1, subTask.Id, Version(subTask.Version), 1)).Status);
    }

    [Fact]
    public async Task Public_visibility_and_wrong_parents_do_not_grant_access_to_subtasks_or_history()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context, null);
        var parent = await context.TaskRequests.SingleAsync();
        parent.Visibility = TaskVisibility.Public;
        context.TaskRequests.Add(TestDatabase.Task(2));
        await context.SaveChangesAsync();
        var service = TaskCollaborationTestServices.SubTasks(context);
        var subTask = await StartDelegatedAsync(service, 2, "Confidential work");
        subTask = Data(await service.SubmitAsync(1, subTask.Id, new() { Version = subTask.Version, Content = "Evidence" }, 2));
        var controller = new SubTaskController(service, new CurrentUser(3));
        Assert.IsType<NotFoundObjectResult>(await controller.List(1));
        Assert.IsType<NotFoundObjectResult>(await controller.Get(1, subTask.Id));
        Assert.IsType<NotFoundObjectResult>(await controller.SubmissionHistory(1, subTask.Id));
        Assert.IsType<NotFoundObjectResult>(await controller.Activity(1, subTask.Id));

        controller = new SubTaskController(service, new CurrentUser(1));
        Assert.IsType<NotFoundObjectResult>(await controller.Get(2, subTask.Id));
        Assert.IsType<NotFoundObjectResult>(await controller.SubmissionHistory(2, subTask.Id));
        Assert.IsType<NotFoundObjectResult>(await controller.Activity(2, subTask.Id));
        Assert.IsType<NotFoundObjectResult>(await controller.Unassign(2, subTask.Id, Version(subTask.Version)));
        Assert.IsType<NotFoundObjectResult>(await controller.Review(2, subTask.Id, subTask.CurrentSubmissionId!.Value, Review(subTask.Version)));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(1, Details(" \t ")));
        Assert.IsType<BadRequestObjectResult>(await controller.Delete(1, subTask.Id));
        Assert.IsType<ConflictObjectResult>(await controller.Start(1, subTask.Id, Version(0)));
        var forbidden = Assert.IsType<ObjectResult>(await new SubTaskController(service, new CurrentUser(2))
            .Cancel(1, subTask.Id, Version(subTask.Version)));
        Assert.Equal(403, forbidden.StatusCode);
        var created = Assert.IsType<CreatedAtActionResult>(await controller.Create(1, Details()));
        var createdDto = Assert.IsType<SubTaskDto>(created.Value);
        Assert.Equal(createdDto.Id, created.RouteValues!["subTaskId"]);
        Assert.IsType<NoContentResult>(await controller.Delete(1, createdDto.Id, createdDto.Version));
    }

    [Fact]
    public async Task Reassignment_and_cancel_reopen_preserve_history_and_never_reuse_old_submissions()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var subTask = await StartDelegatedAsync(service, 2, "Resettable");
        subTask = Data(await service.SubmitAsync(1, subTask.Id, new() { Version = subTask.Version, Content = "Original" }, 2));
        var firstId = subTask.CurrentSubmissionId!.Value;
        subTask = Data(await service.ReviewAsync(1, subTask.Id, firstId,
            Review(subTask.Version, TaskReviewDecision.ChangesRequested, "Try again"), 1));
        subTask = Data(await service.AssignAsync(1, subTask.Id, new() { Version = subTask.Version, AssigneeUserId = 3 }, 1));
        Assert.Equal(TaskStatus.Pending, subTask.Status);
        var reset = Assert.Single(Data(await service.GetActivityHistoryAsync(1, subTask.Id, 1)),
            x => x.ActivityType == TaskActivityType.ResponsibilityReset);
        Assert.Equal(TaskStatus.InProgress, reset.FromStatus);
        Assert.Equal(TaskStatus.Pending, reset.ToStatus);
        Assert.Equal(firstId, Assert.Single(Data(await service.GetSubmissionHistoryAsync(1, subTask.Id, 1))).Id);
        subTask = Data(await service.StartAsync(1, subTask.Id, Version(subTask.Version), 3));
        subTask = Data(await service.SubmitAsync(1, subTask.Id, new() { Version = subTask.Version, Content = "Abandoned" }, 3));
        var abandonedId = subTask.CurrentSubmissionId!.Value;
        subTask = Data(await service.CancelAsync(1, subTask.Id, Version(subTask.Version), 1));
        subTask = Data(await service.ReopenAsync(1, subTask.Id, Version(subTask.Version), 1));
        subTask = Data(await service.StartAsync(1, subTask.Id, Version(subTask.Version), 3));
        subTask = Data(await service.SubmitAsync(1, subTask.Id, new() { Version = subTask.Version, Content = "Current" }, 3));
        Assert.IsAssignableFrom<IConflictResult>(
            await service.ReviewAsync(1, subTask.Id, abandonedId, Review(subTask.Version), 1));
        var history = Data(await service.GetSubmissionHistoryAsync(1, subTask.Id, 1));
        Assert.Equal([3, 2, 1], history.Select(x => x.RevisionNumber));
        Assert.Null(history[1].Review);
    }

    [Theory]
    [InlineData(TaskStatus.Pending)]
    [InlineData(TaskStatus.InProgress)]
    [InlineData(TaskStatus.InReview)]
    public async Task Parent_completion_and_parent_approval_are_blocked_by_each_open_child_status(TaskStatus status)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context);
        var parent = await context.TaskRequests.SingleAsync();
        var child = new SubTask { TaskRequestId = 1, CreatedByUserId = 1, Title = "Child", Description = "Open", Status = status };
        context.SubTasks.Add(child);
        await context.SaveChangesAsync();
        var tasks = TaskCollaborationTestServices.TaskRequests(context);
        Assert.IsAssignableFrom<IConflictResult>(await tasks.CompleteTaskAsync(1, new() { Version = parent.Version }, 1));
        parent.AssigneeUserId = 2;
        parent.Status = TaskStatus.InReview;
        var submission = new TaskSubmission { TaskRequestId = 1, SubmittedByUserId = 2, RevisionNumber = 1, Content = "Parent work" };
        context.TaskSubmissions.Add(submission);
        await context.SaveChangesAsync();
        var review = new ReviewTaskSubmissionDto { Version = parent.Version, Decision = TaskReviewDecision.Approved };
        Assert.IsAssignableFrom<IConflictResult>(await tasks.ReviewAsync(1, submission.Id, review, 1));
        Assert.Empty(await context.TaskSubmissionReviews.ToListAsync());
        Assert.Empty(await context.TaskActivities.ToListAsync());

        child.Status = TaskStatus.Cancelled;
        await context.SaveChangesAsync();
        Assert.True((await tasks.ReviewAsync(1, submission.Id, review, 1)).Success);
        Assert.Equal(TaskStatus.Completed, parent.Status);
    }

    [Theory]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Cancelled)]
    [InlineData(TaskStatus.InReview)]
    public async Task Locked_parent_allows_history_but_blocks_new_and_existing_subtask_mutations(TaskStatus status)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var subTask = await StartDelegatedAsync(service, 2, "Child");
        subTask = Data(await service.SubmitAsync(1, subTask.Id, new() { Version = subTask.Version, Content = "Evidence" }, 2));
        var parent = await context.TaskRequests.SingleAsync();
        parent.Status = status;
        await context.SaveChangesAsync();
        Assert.Single(Data(await service.ListAsync(1, 2)));
        Assert.Single(Data(await service.GetSubmissionHistoryAsync(1, subTask.Id, 2)));
        Assert.NotEmpty(Data(await service.GetActivityHistoryAsync(1, subTask.Id, 2)));
        Assert.IsAssignableFrom<IConflictResult>(await service.CreateAsync(1, Details(), 1));
        Assert.IsAssignableFrom<IConflictResult>(await service.CancelAsync(1, subTask.Id, Version(subTask.Version), 1));
        Assert.IsAssignableFrom<IConflictResult>(await service.DeleteAsync(1, subTask.Id, Version(subTask.Version), 1));
        Assert.IsAssignableFrom<IConflictResult>(await service.ReviewAsync(1, subTask.Id, subTask.CurrentSubmissionId!.Value,
            Review(subTask.Version), 1));
    }

    [Fact]
    public async Task Soft_deleting_subtasks_or_parent_hides_history_and_preserves_audit_and_evidence()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var subTask = await StartDelegatedAsync(service, 2, "Child");
        subTask = Data(await service.SubmitAsync(1, subTask.Id, new() { Version = subTask.Version, Content = "Evidence" }, 2));
        Assert.True((await service.DeleteAsync(1, subTask.Id, Version(subTask.Version), 1)).Success);
        Assert.Empty(Data(await service.ListAsync(1, 1)));
        Assert.Equal(Messages.DataNotFound, (await service.GetSubmissionHistoryAsync(1, subTask.Id, 1)).Message);
        Assert.Single(await context.SubTaskSubmissions.ToListAsync());
        Assert.Single(await context.SubTaskActivities.Where(x => x.ActivityType == TaskActivityType.TaskDeleted).ToListAsync());
        Assert.Equal(1, (await context.SubTasks.SingleAsync()).DeletedByUserId);
        var second = Data(await service.CreateAsync(1, Details(), 1));
        second = Data(await service.CompleteAsync(1, second.Id, Version(second.Version), 1));
        var tasks = TaskCollaborationTestServices.TaskRequests(context);
        Assert.True((await tasks.CompleteTaskAsync(1, new() { Version = 0 }, 1)).Success);
        Assert.True((await tasks.DeleteTask(1, 1)).Success);
        Assert.Equal(Messages.DataNotFound, (await service.ListAsync(1, 2)).Message);
        Assert.Equal(Messages.DataNotFound, (await service.GetActivityHistoryAsync(1, second.Id, 1)).Message);
        Assert.Equal(Messages.DataNotFound, (await service.CreateAsync(1, Details(), 1)).Message);
    }

    private sealed class CurrentUser(int id) : ICurrentUserService
    {
        public int UserId => id;
    }
}
