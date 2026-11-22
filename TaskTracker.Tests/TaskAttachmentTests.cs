using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskTracker.API.Controllers;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public sealed class TaskAttachmentTests
{
    [Fact]
    public async Task Required_scan_fails_closed_when_scanner_is_not_configured()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        await context.SaveChangesAsync();
        var store = new RecordingStore { BeforeSave = () => throw new InvalidOperationException("File must not be stored.") };
        var controller = new TaskAttachmentController(context, new CurrentUser(1),
            TaskCollaborationTestServices.Create(context), store, new NoOpTaskFileScanner(),
            Options.Create(new TaskFileOptions { RequireCleanScan = true }), new EfSubTaskDal(context));
        await using var data = new MemoryStream("Reference"u8.ToArray());
        var result = await controller.UploadTask(1, new FormFile(data, 0, data.Length, "file", "notes.txt"), CancellationToken.None);
        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(await context.TaskAttachments.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submission_upload_and_review_cannot_commit_against_the_same_version(bool reviewWins)
    {
        using var database = new TestDatabase();
        await AssertUploadReviewRace(() => database.CreateContext(), reviewWins);
    }

    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostgreSql_submission_upload_and_review_have_one_winner(bool reviewWins)
    {
        await using var database = await PostgreSqlTestDatabase.CreateCurrentModelAsync();
        await AssertUploadReviewRace(() => database.CreateContext(), reviewWins);
    }

    private static async Task AssertUploadReviewRace(Func<TaskTrackerDbContext> createContext, bool reviewWins)
    {
        await using var setup = createContext();
        var task = TestDatabase.Task();
        task.Status = TaskStatus.InReview;
        task.AssigneeUserId = 2;
        setup.TaskRequests.Add(task);
        setup.TaskShares.Add(new TaskShare { TaskRequestId = task.Id, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        var revision = new TaskSubmission { TaskRequestId = task.Id, SubmittedByUserId = 2, RevisionNumber = 1, Content = "Delivery" };
        setup.TaskSubmissions.Add(revision);
        await setup.SaveChangesAsync();

        async Task<IDataResult<TaskSubmissionReviewDto>> Review(long version)
        {
            await using var context = createContext();
            return await TaskCollaborationTestServices.TaskRequests(context).ReviewAsync(task.Id, revision.Id,
                new ReviewTaskSubmissionDto { Version = version, Decision = TaskReviewDecision.Approved }, 1);
        }

        var store = new RecordingStore();
        if (reviewWins) store.BeforeSave = async () => Assert.True((await Review(task.Version)).Success);
        await using var uploadContext = createContext();
        var controller = new TaskAttachmentController(uploadContext, new CurrentUser(2),
            TaskCollaborationTestServices.Create(uploadContext), store, new CleanScanner(),
            Options.Create(new TaskFileOptions()), new EfSubTaskDal(uploadContext));
        await using var data = new MemoryStream("Review evidence"u8.ToArray());
        var upload = await controller.UploadSubmission(task.Id, revision.Id,
            new FormFile(data, 0, data.Length, "file", "evidence.txt"), CancellationToken.None);

        await using var verify = createContext();
        if (reviewWins)
        {
            Assert.IsType<ConflictObjectResult>(upload);
            Assert.Empty(await verify.TaskAttachments.ToListAsync());
            Assert.True(store.Deleted);
        }
        else
        {
            Assert.IsType<CreatedAtActionResult>(upload);
            Assert.IsAssignableFrom<IConflictResult>(await Review(task.Version));
            Assert.Empty(await verify.TaskSubmissionReviews.ToListAsync());
            Assert.False(store.Deleted);
            Assert.Single(await verify.TaskAttachments.ToListAsync());
            Assert.True((await Review(task.Version + 1)).Success);
        }
        Assert.Single(await verify.TaskSubmissionReviews.ToListAsync());
        Assert.Empty(await verify.TaskPlanningRevisions.ToListAsync());
    }

    [Fact]
    public async Task Parent_attachment_routes_do_not_expose_subtask_submission_evidence()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SubTaskWorkflowTests.SeedPersonalAsync(context);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var subTask = await SubTaskWorkflowTests.StartDelegatedAsync(service, 2, "Subtask evidence");
        subTask = SubTaskWorkflowTests.Data(await service.SubmitAsync(1, subTask.Id,
            new() { Version = subTask.Version, Content = "Evidence" }, 2));
        var attachment = new TaskAttachment
        {
            TaskRequestId = 1, UploaderUserId = 2, Kind = TaskAttachmentKind.SubTaskSubmission,
            SubTaskSubmissionId = subTask.CurrentSubmissionId, OriginalFileName = "private.txt",
            StorageKey = new string('s', 32), ContentType = "text/plain", SizeBytes = 5
        };
        context.TaskAttachments.Add(attachment);
        await context.SaveChangesAsync();
        var controller = new TaskAttachmentController(context, new CurrentUser(1),
            TaskCollaborationTestServices.Create(context), new RecordingStore(), new NoOpTaskFileScanner(),
            Options.Create(new TaskFileOptions()), new EfSubTaskDal(context));
        var list = Assert.IsType<OkObjectResult>(await controller.List(1));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<TaskAttachmentDto>>(list.Value));
        Assert.IsType<NotFoundResult>(await controller.Download(attachment.Id));
        Assert.IsType<NotFoundResult>(await controller.Delete(attachment.Id));
        Assert.Single(await context.TaskAttachments.ToListAsync());
    }

    [Fact]
    public async Task Attachment_context_must_match_its_kind()
    {
        using var database = new TestDatabase(); using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskAttachments.Add(new TaskAttachment { TaskRequestId = 1, UploaderUserId = 1,
            Kind = TaskAttachmentKind.Submission, OriginalFileName = "evidence.pdf", StorageKey = new string('a', 32),
            ContentType = "application/pdf", SizeBytes = 10 });
        await Assert.ThrowsAsync<ValidationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Discussion_attachment_is_immutable_workflow_evidence()
    {
        using var database = new TestDatabase(); using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskMessages.Add(new TaskMessage { TaskRequestId = 1, SenderUserId = 1, Content = "Document attached" });
        await context.SaveChangesAsync();
        var messageId = await context.TaskMessages.Select(x => x.Id).SingleAsync();
        var attachment = new TaskAttachment { TaskRequestId = 1, UploaderUserId = 1, Kind = TaskAttachmentKind.Message,
            TaskMessageId = messageId, OriginalFileName = "notes.txt", StorageKey = new string('b', 32),
            ContentType = "text/plain", SizeBytes = 5 };
        context.TaskAttachments.Add(attachment); await context.SaveChangesAsync();
        attachment.OriginalFileName = "renamed.txt";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    private sealed class CurrentUser(int userId) : ICurrentUserService
    {
        public int UserId => userId;
    }

    private sealed class CleanScanner : ITaskFileScanner
    {
        public Task<TaskFileScanVerdict> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(TaskFileScanVerdict.Clean);
    }

    private sealed class RecordingStore : ITaskFileStore
    {
        public Func<Task>? BeforeSave { get; set; }
        public bool Deleted { get; private set; }

        public async Task<string> SaveAsync(Stream source, CancellationToken cancellationToken)
        {
            if (BeforeSave is not null) await BeforeSave();
            return new string('c', 32);
        }

        public Stream OpenRead(string key) => throw new NotSupportedException();

        public void Delete(string key) => Deleted = true;
    }
}
