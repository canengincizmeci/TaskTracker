using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class PostgreSqlSubmissionConcurrencyTests
{
    private static async Task SeedDelegated(PostgreSqlTestDatabase database, TaskStatus status)
    {
        await using var context = database.CreateContext();
        var task = TestDatabase.Task(); task.AssigneeUserId = 2; task.Status = status;
        context.TaskRequests.Add(task);
        context.TaskShares.AddRange(
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.Edit },
            new TaskShare { TaskRequestId = 1, SharedWithUserId = 3, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
    }

    [PostgreSqlFact]
    public async Task Parallel_submissions_have_one_atomic_winner()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedDelegated(database, TaskStatus.InProgress);
        var barrier = new SaveBarrierInterceptor("max(");
        await using var firstContext = database.CreateContext(barrier);
        await using var secondContext = database.CreateContext(barrier);

        var results = await Task.WhenAll(
            WorkspaceTestServices.TaskRequests(firstContext).SubmitAsync(1, new() { Version = 0, Content = "First" }, 2),
            WorkspaceTestServices.TaskRequests(secondContext).SubmitAsync(1, new() { Version = 0, Content = "Second" }, 2));

        Assert.Single(results, x => x.Success);
        Assert.IsAssignableFrom<IConflictResult>(Assert.Single(results, x => !x.Success));
        await using var verify = database.CreateContext();
        Assert.Equal(TaskStatus.InReview, (await verify.TaskRequests.SingleAsync()).Status);
        Assert.Single(await verify.TaskSubmissions.ToListAsync());
        Assert.Single(await verify.TaskActivities.Where(x => x.ActivityType == TaskActivityType.SubmissionCreated).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Competing_review_decisions_have_one_atomic_winner()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedDelegated(database, TaskStatus.InReview);
        await using (var seed = database.CreateContext())
        {
            seed.TaskSubmissions.Add(new TaskSubmission
            { TaskRequestId = 1, SubmittedByUserId = 2, RevisionNumber = 1, Content = "Work" });
            await seed.SaveChangesAsync();
        }
        var barrier = new SaveBarrierInterceptor("TaskSubmissionReviews");
        await using var approveContext = database.CreateContext(barrier);
        await using var changesContext = database.CreateContext(barrier);

        var results = await Task.WhenAll(
            WorkspaceTestServices.TaskRequests(approveContext).ReviewAsync(1, 1,
                new() { Version = 0, Decision = TaskReviewDecision.Approved }, 1),
            WorkspaceTestServices.TaskRequests(changesContext).ReviewAsync(1, 1,
                new() { Version = 0, Decision = TaskReviewDecision.ChangesRequested, Feedback = "Revise" }, 1));

        Assert.Single(results, x => x.Success);
        Assert.IsAssignableFrom<IConflictResult>(Assert.Single(results, x => !x.Success));
        await using var verify = database.CreateContext();
        var review = Assert.Single(await verify.TaskSubmissionReviews.ToListAsync());
        var task = await verify.TaskRequests.SingleAsync();
        Assert.Equal(review.Decision == TaskReviewDecision.Approved ? TaskStatus.Completed : TaskStatus.InProgress, task.Status);
        Assert.Single(await verify.TaskActivities.Where(x => x.ActivityType == TaskActivityType.SubmissionApproved ||
            x.ActivityType == TaskActivityType.ChangesRequested).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Review_and_cancellation_have_one_atomic_winner()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedDelegated(database, TaskStatus.InReview);
        await using (var seed = database.CreateContext())
        {
            seed.TaskSubmissions.Add(new TaskSubmission
            { TaskRequestId = 1, SubmittedByUserId = 2, RevisionNumber = 1, Content = "Work" });
            await seed.SaveChangesAsync();
        }
        var barrier = new SaveBarrierInterceptor("TaskActivities");
        await using var reviewContext = database.CreateContext(barrier);
        await using var cancelContext = database.CreateContext(barrier);

        var reviewTask = WorkspaceTestServices.TaskRequests(reviewContext).ReviewAsync(1, 1,
            new() { Version = 0, Decision = TaskReviewDecision.Approved }, 1);
        var cancelTask = WorkspaceTestServices.TaskRequests(cancelContext).CancelTaskAsync(1, new() { Version = 0 }, 1);
        await Task.WhenAll(reviewTask, cancelTask);

        var results = new IResult[] { reviewTask.Result, cancelTask.Result };
        Assert.Single(results, x => x.Success);
        Assert.IsAssignableFrom<IConflictResult>(Assert.Single(results, x => !x.Success));
        await using var verify = database.CreateContext();
        var task = await verify.TaskRequests.SingleAsync();
        Assert.Contains(task.Status, new[] { TaskStatus.Completed, TaskStatus.Cancelled });
        Assert.Equal(task.Status == TaskStatus.Completed ? 1 : 0, await verify.TaskSubmissionReviews.CountAsync());
        Assert.Single(await verify.TaskActivities.ToListAsync());
    }

    [PostgreSqlTheory]
    [InlineData("reassign")]
    [InlineData("remove")]
    [InlineData("downgrade")]
    public async Task Submission_and_responsibility_change_have_one_atomic_winner(string operation)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedDelegated(database, TaskStatus.InProgress);
        var barrier = new SaveBarrierInterceptor("TaskActivities");
        await using var submitContext = database.CreateContext(barrier);
        await using var responsibilityContext = database.CreateContext(barrier);
        var submit = WorkspaceTestServices.TaskRequests(submitContext).SubmitAsync(1,
            new() { Version = 0, Content = "Concurrent work" }, 2);
        Task<IResult> responsibility = operation switch
        {
            "reassign" => WorkspaceTestServices.TaskRequests(responsibilityContext).AssignTaskAsync(1,
                new() { Version = 0, AssigneeUserId = 3 }, 1),
            "remove" => WorkspaceTestServices.TaskShares(responsibilityContext, 1)
                .RemoveParticipantAsync(1, 2, 0),
            _ => WorkspaceTestServices.TaskShares(responsibilityContext, 1)
                .UpdateParticipantPermissionAsync(1, 2, new() { Version = 0, Permission = TaskPermission.View })
        };
        await Task.WhenAll(submit, responsibility);

        var results = new IResult[] { submit.Result, responsibility.Result };
        Assert.Single(results, x => x.Success);
        Assert.IsAssignableFrom<IConflictResult>(Assert.Single(results, x => !x.Success));
        await using var verify = database.CreateContext();
        var task = await verify.TaskRequests.SingleAsync();
        var submissions = await verify.TaskSubmissions.ToListAsync();
        if (task.Status == TaskStatus.InReview)
        {
            Assert.Single(submissions);
            Assert.Equal(2, task.AssigneeUserId);
            Assert.Equal(TaskPermission.Edit,
                (await verify.TaskShares.SingleAsync(x => x.SharedWithUserId == 2)).Permission);
        }
        else
        {
            Assert.Empty(submissions);
            Assert.Equal(TaskStatus.Pending, task.Status);
        }
    }

    [PostgreSqlFact]
    public async Task Stale_review_cannot_cross_cancel_reopen_and_new_submission()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedDelegated(database, TaskStatus.InReview);
        await using (var seed = database.CreateContext())
        {
            seed.TaskSubmissions.Add(new TaskSubmission
            { TaskRequestId = 1, SubmittedByUserId = 2, RevisionNumber = 1, Content = "Old" });
            await seed.SaveChangesAsync();
        }
        await using var staleContext = database.CreateContext();
        await staleContext.TaskRequests.SingleAsync();
        await staleContext.TaskSubmissions.SingleAsync();
        await using (var current = database.CreateContext())
        {
            var manager = WorkspaceTestServices.TaskRequests(current);
            Assert.True((await manager.CancelTaskAsync(1, new() { Version = 0 }, 1)).Success);
            Assert.True((await manager.ReopenTaskAsync(1, new() { Version = 1 }, 1)).Success);
            Assert.True((await manager.StartTaskAsync(1, new() { Version = 2 }, 2)).Success);
            Assert.True((await manager.SubmitAsync(1, new() { Version = 3, Content = "New" }, 2)).Success);
        }

        var stale = await WorkspaceTestServices.TaskRequests(staleContext).ReviewAsync(1, 1,
            new() { Version = 0, Decision = TaskReviewDecision.Approved }, 1);
        Assert.IsAssignableFrom<IConflictResult>(stale);
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.TaskSubmissionReviews.ToListAsync());
        Assert.Equal(TaskStatus.InReview, (await verify.TaskRequests.SingleAsync()).Status);
    }
}
