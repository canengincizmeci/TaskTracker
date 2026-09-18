using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Tests;

public class WorkflowPersistenceTests
{
    [Fact]
    public async Task Stale_task_update_conflicts_and_rolls_back_its_activity()
    {
        using var database = new TestDatabase();
        using var first = database.CreateContext();
        first.TaskRequests.Add(TestDatabase.Task());
        await first.SaveChangesAsync();
        using var second = database.CreateContext();
        var stale = await second.TaskRequests.SingleAsync();
        var current = await first.TaskRequests.SingleAsync();
        current.Title = "First update";
        first.SaveChanges(); // Exercise synchronous version advancement too.
        Assert.Equal(1, current.Version);
        stale.Title = "Stale update";
        second.TaskActivities.Add(new TaskActivity
        {
            TaskRequestId = 1, ActorUserId = 1, ActivityType = TaskActivityType.TaskDetailsUpdated
        });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        using var verify = database.CreateContext();
        Assert.Equal("First update", (await verify.TaskRequests.SingleAsync()).Title);
        Assert.Empty(await verify.TaskActivities.ToListAsync());
    }

    [Fact]
    public async Task Revision_numbers_and_final_reviews_are_unique()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        var submission = new TaskSubmission { TaskRequestId = 1, SubmittedByUserId = 2, RevisionNumber = 1, Content = "Work" };
        context.TaskSubmissions.Add(submission);
        await context.SaveChangesAsync();

        using (var duplicate = database.CreateContext())
        {
            duplicate.TaskSubmissions.Add(new TaskSubmission { TaskRequestId = 1, SubmittedByUserId = 2, RevisionNumber = 1, Content = "Duplicate" });
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        context.TaskSubmissionReviews.Add(new TaskSubmissionReview
        {
            TaskSubmissionId = submission.Id, ReviewerUserId = 1, Decision = TaskReviewDecision.Approved
        });
        await context.SaveChangesAsync();
        using var secondReview = database.CreateContext();
        secondReview.TaskSubmissionReviews.Add(new TaskSubmissionReview
        {
            TaskSubmissionId = submission.Id, ReviewerUserId = 1, Decision = TaskReviewDecision.ChangesRequested, Feedback = "Revise"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => secondReview.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_cannot_be_updated_or_deleted(bool delete)
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var submission = new TaskSubmission { TaskRequest = TestDatabase.Task(), SubmittedByUserId = 2, RevisionNumber = 1, Content = "Work" };
        var review = new TaskSubmissionReview { TaskSubmission = submission, ReviewerUserId = 1, Decision = TaskReviewDecision.Approved };
        var activity = new TaskActivity { TaskRequest = submission.TaskRequest, ActorUserId = 1, Review = review, Submission = submission, ActivityType = TaskActivityType.SubmissionApproved };
        context.TaskActivities.Add(activity);
        await context.SaveChangesAsync();

        foreach (var (type, id) in new[] { (typeof(TaskSubmission), submission.Id), (typeof(TaskSubmissionReview), review.Id), (typeof(TaskActivity), activity.Id) })
        {
            // Load only this row so tracked dependent relationships cannot reject
            // deletion before the shared SaveChanges guard gets exercised.
            using var mutation = database.CreateContext();
            var entity = (await mutation.FindAsync(type, id))!;
            mutation.Entry(entity).State = delete ? EntityState.Deleted : EntityState.Modified;
            await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.SaveChangesAsync());
            Assert.Throws<InvalidOperationException>(() => mutation.SaveChanges());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public async Task Persistence_guard_rejects_invalid_submission_content(int length)
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.TaskSubmissions.Add(new TaskSubmission
        {
            TaskRequest = TestDatabase.Task(), SubmittedByUserId = 2, RevisionNumber = 1, Content = new string('x', length)
        });
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Persistence_guard_requires_feedback_for_changes_requested()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var submission = new TaskSubmission
        {
            TaskRequest = TestDatabase.Task(), SubmittedByUserId = 2, RevisionNumber = 1, Content = "Work"
        };
        context.TaskSubmissions.Add(submission);
        await context.SaveChangesAsync();

        context.TaskSubmissionReviews.Add(new TaskSubmissionReview
        {
            TaskSubmissionId = submission.Id, ReviewerUserId = 1, Decision = TaskReviewDecision.ChangesRequested, Feedback = " "
        });
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Historical_author_cannot_be_deleted()
    {
        using var database = new TestDatabase();
        using (var seed = database.CreateContext())
        {
            seed.TaskSubmissions.Add(new TaskSubmission
            {
                TaskRequest = TestDatabase.Task(), SubmittedByUserId = 2, RevisionNumber = 1, Content = "Work"
            });
            await seed.SaveChangesAsync();
        }
        using var context = database.CreateContext();
        context.Users.Remove(await context.Users.SingleAsync(x => x.Id == 2));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
