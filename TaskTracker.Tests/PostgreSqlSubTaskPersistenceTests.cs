using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Tests;

public class PostgreSqlSubTaskPersistenceTests
{
    [PostgreSqlFact]
    public async Task Upgrade_preserves_existing_parent_submission_and_attachment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("20261117181846_AddInvitationMessagesAndTaskAttachments");
        await using var context = database.CreateContext();
        var parent = TestDatabase.Task();
        context.TaskRequests.Add(parent);
        var submission = new TaskSubmission
        {
            TaskRequestId = parent.Id, SubmittedByUserId = 2, RevisionNumber = 1, Content = "Original evidence"
        };
        context.TaskSubmissions.Add(submission);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TaskAttachments" ("TaskRequestId", "UploaderUserId", "Kind", "TaskSubmissionId",
                "OriginalFileName", "StorageKey", "ContentType", "SizeBytes", "CreatedAt", "IsDeleted")
            VALUES ({parent.Id}, 2, 2, {submission.Id}, 'work.txt', 'original', 'text/plain', 4, CURRENT_TIMESTAMP, FALSE)
            """);

        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(0, (await context.TaskRequests.SingleAsync()).Version);
        Assert.Equal(submission.Content, (await context.TaskSubmissions.SingleAsync()).Content);
        var original = await context.TaskAttachments.SingleAsync();
        Assert.Equal(TaskAttachmentKind.Submission, original.Kind);
        Assert.Equal(submission.Id, original.TaskSubmissionId);
        Assert.Null(original.SubTaskSubmissionId);
        Assert.Empty(await context.SubTasks.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Migrated_schema_enforces_revision_review_audit_and_parent_scope_constraints()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        context.TaskRequests.AddRange(TestDatabase.Task(1), TestDatabase.Task(2));
        var first = new SubTask { TaskRequestId = 1, CreatedByUserId = 1, Title = "First", Description = "One" };
        var sibling = new SubTask { TaskRequestId = 1, CreatedByUserId = 1, Title = "Second", Description = "Two" };
        context.SubTasks.AddRange(first, sibling);
        await context.SaveChangesAsync();
        var firstRevision = new SubTaskSubmission
        {
            TaskRequestId = 1, SubTaskId = first.Id, SubmittedByUserId = 2, RevisionNumber = 1, Content = "First delivery"
        };
        var siblingRevision = new SubTaskSubmission
        {
            TaskRequestId = 1, SubTaskId = sibling.Id, SubmittedByUserId = 2, RevisionNumber = 1, Content = "Second delivery"
        };
        context.SubTaskSubmissions.AddRange(firstRevision, siblingRevision);
        await context.SaveChangesAsync();
        var review = new SubTaskSubmissionReview
        {
            SubTaskSubmissionId = firstRevision.Id, ReviewerUserId = 1, Decision = TaskReviewDecision.Approved
        };
        var attachment = new TaskAttachment
        {
            TaskRequestId = 1, SubTaskSubmissionId = firstRevision.Id, UploaderUserId = 2, Kind = TaskAttachmentKind.SubTaskSubmission,
            OriginalFileName = "work.txt", StorageKey = "evidence", ContentType = "text/plain", SizeBytes = 4
        };
        var activity = new SubTaskActivity
        {
            TaskRequestId = 1, SubTaskId = first.Id, ActorUserId = 2, ActivityType = TaskActivityType.SubmissionCreated,
            SubmissionId = firstRevision.Id
        };
        context.AddRange(review, attachment, activity);
        await context.SaveChangesAsync();

        async Task Reject(string sqlState, FormattableString sql)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(sql));
            Assert.Equal(sqlState, error.SqlState);
        }

        await Reject(PostgresErrorCodes.UniqueViolation, $"""
            INSERT INTO "SubTaskSubmissions" ("TaskRequestId", "SubTaskId", "SubmittedByUserId", "RevisionNumber", "Content")
            VALUES (1, {first.Id}, 2, 1, 'Duplicate revision')
            """);
        await Reject(PostgresErrorCodes.UniqueViolation, $"""
            INSERT INTO "SubTaskSubmissionReviews" ("SubTaskSubmissionId", "ReviewerUserId", "Decision")
            VALUES ({firstRevision.Id}, 1, 'Approved')
            """);
        await Reject(PostgresErrorCodes.CheckViolation, $"""
            UPDATE "SubTaskSubmissionReviews" SET "Decision" = 'Invalid' WHERE "Id" = {review.Id}
            """);
        await Reject(PostgresErrorCodes.CheckViolation, $"""
            UPDATE "SubTaskSubmissionReviews" SET "Decision" = 'ChangesRequested', "Feedback" = {"\t\n\u0085\u2028\u2029"} WHERE "Id" = {review.Id}
            """);
        await Reject(PostgresErrorCodes.CheckViolation, $"""
            UPDATE "SubTasks" SET "DeletedByUserId" = 2 WHERE "Id" = {first.Id}
            """);
        await Reject(PostgresErrorCodes.CheckViolation, $"""
            UPDATE "TaskAttachments" SET "Kind" = 0 WHERE "Id" = {attachment.Id}
            """);
        await Reject(PostgresErrorCodes.ForeignKeyViolation, $"""
            INSERT INTO "SubTaskSubmissions" ("TaskRequestId", "SubTaskId", "SubmittedByUserId", "RevisionNumber", "Content")
            VALUES (2, {first.Id}, 2, 2, 'Wrong parent')
            """);
        await Reject(PostgresErrorCodes.ForeignKeyViolation, $"""
            UPDATE "TaskAttachments" SET "TaskRequestId" = 2 WHERE "Id" = {attachment.Id}
            """);
        await Reject(PostgresErrorCodes.ForeignKeyViolation, $"""
            UPDATE "SubTaskActivities" SET "TaskRequestId" = 2 WHERE "Id" = {activity.Id}
            """);
        await Reject(PostgresErrorCodes.ForeignKeyViolation, $"""
            UPDATE "SubTaskActivities" SET "SubmissionId" = {siblingRevision.Id} WHERE "Id" = {activity.Id}
            """);
        await Reject(PostgresErrorCodes.ForeignKeyViolation, $"""
            INSERT INTO "SubTaskActivities" ("TaskRequestId", "SubTaskId", "ActorUserId", "ActivityType", "SubmissionId", "ReviewId")
            VALUES (1, {sibling.Id}, 1, 'SubmissionApproved', {siblingRevision.Id}, {review.Id})
            """);
    }
}
