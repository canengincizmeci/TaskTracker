using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using static TaskTracker.Tests.SubTaskWorkflowTests;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class PostgreSqlSubTaskWorkflowConcurrencyTests
{
    [PostgreSqlFact]
    public async Task Concurrent_submissions_and_reviews_each_commit_exactly_one_transition_and_audit_record()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        int subTaskId;
        long version;
        await using (var seed = database.CreateContext())
        {
            await SeedPersonalAsync(seed);
            var work = await StartDelegatedAsync(TaskCollaborationTestServices.SubTasks(seed), 2, "Parallel revisions");
            (subTaskId, version) = (work.Id, work.Version);
        }

        var submitBarrier = new SaveBarrierInterceptor("""ORDER BY s."RevisionNumber" DESC""");
        await using var first = database.CreateContext(submitBarrier);
        await using var second = database.CreateContext(submitBarrier);
        var submissions = await Task.WhenAll(
            TaskCollaborationTestServices.SubTasks(first).SubmitAsync(1, subTaskId, new() { Version = version, Content = "First" }, 2),
            TaskCollaborationTestServices.SubTasks(second).SubmitAsync(1, subTaskId, new() { Version = version, Content = "Second" }, 2));
        var inReview = Data(Assert.Single(submissions, x => x.Success));
        Assert.IsAssignableFrom<IConflictResult>(Assert.Single(submissions, x => !x.Success));

        var reviewBarrier = new SaveBarrierInterceptor("FROM \"SubTaskSubmissionReviews\"");
        await using var approve = database.CreateContext(reviewBarrier);
        await using var changes = database.CreateContext(reviewBarrier);
        var submissionId = inReview.CurrentSubmissionId!.Value;
        var reviews = await Task.WhenAll(
            TaskCollaborationTestServices.SubTasks(approve).ReviewAsync(1, subTaskId, submissionId, Review(inReview.Version), 1),
            TaskCollaborationTestServices.SubTasks(changes).ReviewAsync(1, subTaskId, submissionId,
                Review(inReview.Version, TaskReviewDecision.ChangesRequested, "Revise"), 1));
        Assert.Single(reviews, x => x.Success);
        Assert.IsAssignableFrom<IConflictResult>(Assert.Single(reviews, x => !x.Success));

        await using var verify = database.CreateContext();
        var revision = Assert.Single(await verify.SubTaskSubmissions.ToListAsync());
        Assert.Equal(1, revision.RevisionNumber);
        var review = Assert.Single(await verify.SubTaskSubmissionReviews.ToListAsync());
        Assert.Equal(revision.Id, review.SubTaskSubmissionId);
        Assert.Equal(review.Decision == TaskReviewDecision.Approved ? TaskStatus.Completed : TaskStatus.InProgress,
            (await verify.SubTasks.SingleAsync()).Status);
        Assert.Equal(version + 2, (await verify.SubTasks.SingleAsync()).Version);
        Assert.Equal(0, (await verify.TaskRequests.SingleAsync()).Version);
        Assert.Single(await verify.SubTaskActivities.Where(x => x.ActivityType == TaskActivityType.SubmissionCreated).ToListAsync());
        Assert.Single(await verify.SubTaskActivities.Where(x => x.ReviewId != null).ToListAsync());

        // The failed command must not leave staged submissions or reviews for a later save.
        var failedContext = submissions[0].Success ? second : first;
        Assert.True((await TaskCollaborationTestServices.SubTasks(failedContext).CreateAsync(1, Details("Retry"), 1)).Success);
        Assert.Equal(1, await verify.SubTaskSubmissions.CountAsync());
    }

    [PostgreSqlTheory]
    [InlineData("create", true)]
    [InlineData("create", false)]
    [InlineData("reopen", true)]
    [InlineData("reopen", false)]
    [InlineData("start", true)]
    [InlineData("start", false)]
    public async Task Parent_completion_cannot_race_create_reopen_or_start_into_an_invalid_state(string operation,
        bool parentFirst)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        int subTaskId = 0;
        await using (var seed = database.CreateContext())
        {
            seed.TaskRequests.Add(TestDatabase.Task());
            if (operation != "create")
            {
                var child = new SubTask
                {
                    TaskRequestId = 1, CreatedByUserId = 1, Title = "Child", Description = "Concurrently changed",
                    Status = operation == "reopen" ? TaskStatus.Completed : TaskStatus.Pending
                };
                seed.SubTasks.Add(child);
                await seed.SaveChangesAsync();
                subTaskId = child.Id;
            }
            else await seed.SaveChangesAsync();
        }

        var firstCommand = new PostgreSqlCommandGate(parentFirst ? "FROM \"SubTasks\" AS" :
            operation == "create" ? "INSERT INTO \"SubTasks\"" : "UPDATE \"SubTasks\"", pause: true);
        var secondCommand = new PostgreSqlCommandGate(parentFirst ? """FROM "TaskRequests" WHERE""" : "FOR NO KEY UPDATE", pause: false);
        await using var parentContext = database.CreateContext(parentFirst ? firstCommand : secondCommand);
        await using var childContext = database.CreateContext(parentFirst ? secondCommand : firstCommand);
        var parent = TaskCollaborationTestServices.TaskRequests(parentContext);
        var childService = TaskCollaborationTestServices.SubTasks(childContext);
        Task<IResult> Complete() => parent.CompleteTaskAsync(1, new() { Version = 0 }, 1);
        async Task<IResult> Change() => operation switch
        {
            "create" => await childService.CreateAsync(1, Details(), 1),
            "reopen" => await childService.ReopenAsync(1, subTaskId, Version(0), 1),
            _ => await childService.StartAsync(1, subTaskId, Version(0), 1)
        };

        var completedFirst = parentFirst ? Complete() : Change();
        Task<IResult> completedSecond;
        try
        {
            await firstCommand.Arrived;
            completedSecond = parentFirst ? Change() : Complete();
            await secondCommand.Arrived;
        }
        finally { firstCommand.Release(); }
        var results = await Task.WhenAll(completedFirst, completedSecond);
        var completion = results[parentFirst ? 0 : 1];
        var mutation = results[parentFirst ? 1 : 0];
        Assert.Equal(parentFirst && operation != "start", completion.Success);
        Assert.NotEqual(completion.Success, mutation.Success);
        Assert.IsAssignableFrom<IConflictResult>(completion.Success ? mutation : completion);

        await using var verify = database.CreateContext();
        var parentState = await verify.TaskRequests.SingleAsync();
        var openChildren = await verify.SubTasks.CountAsync(x => x.Activity &&
            (x.Status == TaskStatus.Pending || x.Status == TaskStatus.InProgress || x.Status == TaskStatus.InReview));
        Assert.Equal(completion.Success ? TaskStatus.Completed : TaskStatus.Pending, parentState.Status);
        Assert.Equal(completion.Success ? 0 : 1, openChildren);
    }

    [PostgreSqlTheory]
    [InlineData("downgrade", true)]
    [InlineData("downgrade", false)]
    [InlineData("remove", true)]
    [InlineData("remove", false)]
    [InlineData("workspace", true)]
    [InlineData("workspace", false)]
    public async Task Revocation_and_submission_observe_one_another_before_mutating(string operation, bool submitFirst)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        int? workspaceId = null;
        int subTaskId;
        long version;
        await using (var seed = database.CreateContext())
        {
            if (operation == "workspace") workspaceId = await SeedWorkspaceAsync(seed);
            else await SeedPersonalAsync(seed);
            var child = await StartDelegatedAsync(TaskCollaborationTestServices.SubTasks(seed), 2, "Changing access");
            (subTaskId, version) = (child.Id, child.Version);
        }

        var firstCommand = new PostgreSqlCommandGate(submitFirst ? """ORDER BY s."RevisionNumber" DESC""" :
            "FROM \"SubTasks\" AS", pause: true);
        var secondCommand = new PostgreSqlCommandGate(submitFirst ? "FOR NO KEY UPDATE" :
            workspaceId.HasValue ? "FOR SHARE OF w" : """FROM "TaskRequests" WHERE""", pause: false);
        await using var submitContext = database.CreateContext(submitFirst ? firstCommand : secondCommand);
        await using var revokeContext = database.CreateContext(submitFirst ? secondCommand : firstCommand);
        async Task<IResult> Submit() => await TaskCollaborationTestServices.SubTasks(submitContext).SubmitAsync(1, subTaskId,
            new() { Version = version, Content = "Concurrent work" }, 2);
        Task<IResult> Revoke() => operation switch
        {
            "downgrade" => TaskCollaborationTestServices.TaskShares(revokeContext, 1)
                .UpdateParticipantPermissionAsync(1, 2, new() { Permission = TaskPermission.View, Version = 0 }),
            "remove" => TaskCollaborationTestServices.TaskShares(revokeContext, 1).RemoveParticipantAsync(1, 2, 0),
            _ => TaskCollaborationTestServices.Workspaces(revokeContext, 1).RemoveMemberAsync(workspaceId!.Value, 2, new() { Version = 0 })
        };

        var completedFirst = submitFirst ? Submit() : Revoke();
        Task<IResult> completedSecond;
        try
        {
            await firstCommand.Arrived;
            completedSecond = submitFirst ? Revoke() : Submit();
            await secondCommand.Arrived;
        }
        finally { firstCommand.Release(); }
        var results = await Task.WhenAll(completedFirst, completedSecond);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        if (submitFirst) Assert.IsAssignableFrom<IConflictResult>(results[1]);
        await using var verify = database.CreateContext();
        var childState = await verify.SubTasks.SingleAsync();
        Assert.Equal(submitFirst ? TaskStatus.InReview : TaskStatus.Pending, childState.Status);
        Assert.Equal(submitFirst ? 2 : (int?)null, childState.AssigneeUserId);
        Assert.Equal(submitFirst ? 1 : 0, await verify.SubTaskSubmissions.CountAsync());
        Assert.Equal(submitFirst || operation == "downgrade",
            await TaskCollaborationTestServices.Create(verify).CanAccessAsync(1, 2));
    }

}

internal sealed class PostgreSqlCommandGate(string marker, bool pause) : DbCommandInterceptor
{
    private readonly TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Arrived => arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
    public void Release() => released.TrySetResult();

    private async Task ObserveAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (!command.CommandText.Contains(marker, StringComparison.Ordinal)) return;
        arrived.TrySetResult();
        if (pause) await released.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await ObserveAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await ObserveAsync(command, cancellationToken);
        return result;
    }
}
