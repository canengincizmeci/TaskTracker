using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class SubTaskPersistenceTests
{
    [Fact]
    public async Task Siblings_have_independent_versions_and_stale_writes_roll_back_their_activity()
    {
        using var database = new TestDatabase();
        using var current = database.CreateContext();
        current.TaskRequests.Add(TestDatabase.Task());
        var first = NewSubTask(10);
        var second = NewSubTask(11);
        current.SubTasks.AddRange(first, second);
        await current.SaveChangesAsync();

        using var concurrent = database.CreateContext();
        var siblings = await concurrent.SubTasks.OrderBy(x => x.Id).ToListAsync();
        first.Title = "First edit";
        current.SaveChanges();
        siblings[1].AssigneeUserId = 2;
        await concurrent.SaveChangesAsync();
        Assert.Equal(1, first.Version);
        Assert.Equal(1, siblings[1].Version);

        siblings[0].Title = "Stale edit";
        concurrent.SubTaskActivities.Add(new SubTaskActivity
        {
            TaskRequestId = 1, SubTaskId = first.Id, ActorUserId = 1, ActivityType = TaskActivityType.TaskDetailsUpdated
        });
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => concurrent.SaveChangesAsync());

        using var verify = database.CreateContext();
        Assert.Equal(0, (await verify.TaskRequests.SingleAsync()).Version);
        Assert.Equal("First edit", (await verify.SubTasks.SingleAsync(x => x.Id == first.Id)).Title);
        Assert.Equal(2, (await verify.SubTasks.SingleAsync(x => x.Id == second.Id)).AssigneeUserId);
        Assert.Empty(await verify.SubTaskActivities.ToListAsync());
    }

    [Fact]
    public async Task Each_subtask_starts_at_revision_one_and_each_revision_gets_one_decision()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.SubTasks.AddRange(NewSubTask(10), NewSubTask(11));
        var first = Submission(10);
        var sibling = Submission(11);
        context.SubTaskSubmissions.AddRange(first, sibling, Submission(10, revision: 2));
        await context.SaveChangesAsync();

        context.SubTaskSubmissionReviews.AddRange(Approved(first.Id), Approved(sibling.Id));
        await context.SaveChangesAsync();

        using var duplicateRevision = database.CreateContext();
        duplicateRevision.SubTaskSubmissions.Add(Submission(10));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateRevision.SaveChangesAsync());
        using var duplicateReview = database.CreateContext();
        duplicateReview.SubTaskSubmissionReviews.Add(new SubTaskSubmissionReview
        {
            SubTaskSubmissionId = first.Id, ReviewerUserId = 3,
            Decision = TaskReviewDecision.ChangesRequested, Feedback = "Missing diagram"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateReview.SaveChangesAsync());
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task Submission_review_activity_and_evidence_are_immutable(EntityState mutation)
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.SubTasks.Add(NewSubTask(10));
        var submission = Submission(10);
        var attachment = Evidence(null);
        attachment.SubTaskSubmission = submission;
        context.TaskAttachments.Add(attachment);
        await context.SaveChangesAsync();
        Assert.Equal(submission.Id, attachment.SubTaskSubmissionId);
        var review = Approved(submission.Id);
        context.SubTaskSubmissionReviews.Add(review);
        await context.SaveChangesAsync();
        var activity = new SubTaskActivity
        {
            TaskRequestId = 1, SubTaskId = 10, ActorUserId = 1, ActivityType = TaskActivityType.SubmissionApproved,
            SubmissionId = submission.Id, ReviewId = review.Id, FromStatus = TaskStatus.InReview, ToStatus = TaskStatus.Completed
        };
        context.SubTaskActivities.Add(activity);
        await context.SaveChangesAsync();

        foreach (var (type, id) in new[]
        {
            (typeof(SubTaskSubmission), submission.Id), (typeof(SubTaskSubmissionReview), review.Id),
            (typeof(SubTaskActivity), activity.Id), (typeof(TaskAttachment), attachment.Id)
        })
        {
            using var write = database.CreateContext();
            var row = await write.FindAsync(type, id);
            write.Entry(row!).State = mutation;
            await Assert.ThrowsAsync<InvalidOperationException>(() => write.SaveChangesAsync());
            Assert.Throws<InvalidOperationException>(() => write.SaveChanges());
        }
    }

    [Fact]
    public async Task Subtask_evidence_stays_with_its_parent_and_cannot_be_reclassified()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.TaskRequests.AddRange(TestDatabase.Task(1), TestDatabase.Task(2));
        context.SubTasks.Add(NewSubTask(10));
        var submission = Submission(10);
        context.SubTaskSubmissions.Add(submission);
        await context.SaveChangesAsync();

        var evidence = Evidence(submission.Id);
        evidence.TaskRequestId = 2;
        context.TaskAttachments.Add(evidence);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        evidence.TaskRequestId = 1;
        await context.SaveChangesAsync();
        evidence.Kind = TaskAttachmentKind.Task;
        evidence.SubTaskSubmissionId = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(TaskAttachmentKind.SubTaskSubmission, null, null, null)]
    [InlineData(TaskAttachmentKind.SubTaskSubmission, 1, null, 1)]
    [InlineData(TaskAttachmentKind.SubTaskSubmission, null, 1, 1)]
    [InlineData(TaskAttachmentKind.Task, null, null, 1)]
    [InlineData(TaskAttachmentKind.Message, 1, null, 1)]
    [InlineData(TaskAttachmentKind.Submission, null, 1, 1)]
    public async Task Attachment_kind_requires_exactly_its_own_context(TaskAttachmentKind kind, int? messageId,
        int? submissionId, int? subTaskSubmissionId)
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var attachment = Evidence(subTaskSubmissionId);
        attachment.Kind = kind;
        attachment.TaskMessageId = messageId;
        attachment.TaskSubmissionId = submissionId;
        context.TaskAttachments.Add(attachment);
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Deletion_requires_consistent_provenance_on_insert_and_update()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        var subTask = NewSubTask(10);
        subTask.DeletedAt = DateTime.UtcNow;
        context.SubTasks.Add(subTask);
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
        subTask.DeletedAt = null;
        await context.SaveChangesAsync();

        subTask.Activity = false;
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
        subTask.DeletedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
        subTask.DeletedByUserId = 2;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var stored = await context.SubTasks.SingleAsync();
        Assert.False(stored.Activity);
        Assert.Equal(2, stored.DeletedByUserId);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal(1, stored.Version);
        stored.DeletedByUserId = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.SubTasks.Remove(await context.SubTasks.SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Subtask_fields_use_task_limits_and_defined_priority_and_status()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        var subTask = NewSubTask(10);
        context.SubTasks.Add(subTask);
        await context.SaveChangesAsync();

        subTask.Title = " \t";
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
        subTask.Title = new string('a', TaskRequest.MaxTitleLength + 1);
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
        subTask.Title = "Valid";
        subTask.Description = new string('b', TaskRequest.MaxDescriptionLength + 1);
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
        subTask.Description = "A description";
        subTask.Priority = (TaskPriority)99;
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
        subTask.Priority = TaskPriority.High;
        subTask.Status = (TaskStatus)99;
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 10001)]
    public async Task Submission_requires_a_positive_revision_and_bounded_nonblank_content(int revision, int length)
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.SubTaskSubmissions.Add(new SubTaskSubmission
        {
            TaskRequestId = 1, SubTaskId = 10, SubmittedByUserId = 2, RevisionNumber = revision,
            Content = length == 0 ? "\t" : new string('x', length)
        });
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData((TaskReviewDecision)99, null)]
    [InlineData(TaskReviewDecision.ChangesRequested, null)]
    [InlineData(TaskReviewDecision.ChangesRequested, "\t\n")]
    public async Task Review_requires_a_valid_decision_and_feedback_for_requested_changes(TaskReviewDecision decision, string? feedback)
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.SubTaskSubmissionReviews.Add(new SubTaskSubmissionReview
        {
            SubTaskSubmissionId = 1, ReviewerUserId = 1, Decision = decision, Feedback = feedback
        });
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(TaskActivityType.UserInvited, null)]
    [InlineData(TaskActivityType.SubmissionCreated, null)]
    [InlineData(TaskActivityType.TaskDeleted, TaskStatus.InProgress)]
    [InlineData(TaskActivityType.TaskCreated, (TaskStatus)99)]
    public async Task Activity_checks_operation_context_and_statuses(TaskActivityType type, TaskStatus? toStatus)
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.SubTaskActivities.Add(new SubTaskActivity
        {
            TaskRequestId = 1, SubTaskId = 10, ActorUserId = 1, ActivityType = type, ToStatus = toStatus
        });
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
    }

    private static SubTask NewSubTask(int id) => new()
    {
        Id = id, TaskRequestId = 1, CreatedByUserId = 1, Title = $"Part {id}", Description = "A separate deliverable"
    };

    private static SubTaskSubmission Submission(int subTaskId, int revision = 1) => new()
    {
        TaskRequestId = 1, SubTaskId = subTaskId, SubmittedByUserId = 2, RevisionNumber = revision, Content = "Delivered"
    };

    private static SubTaskSubmissionReview Approved(int submissionId) => new()
    {
        SubTaskSubmissionId = submissionId, ReviewerUserId = 1, Decision = TaskReviewDecision.Approved
    };

    private static TaskAttachment Evidence(int? subTaskSubmissionId) => new()
    {
        TaskRequestId = 1, UploaderUserId = 2, Kind = TaskAttachmentKind.SubTaskSubmission,
        SubTaskSubmissionId = subTaskSubmissionId, OriginalFileName = "work.txt", StorageKey = "evidence",
        ContentType = "text/plain", SizeBytes = 4
    };
}
