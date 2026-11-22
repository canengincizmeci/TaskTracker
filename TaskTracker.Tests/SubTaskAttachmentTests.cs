using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using TaskTracker.API.Controllers;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Extensions;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;
using static TaskTracker.Tests.SubTaskWorkflowTests;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class SubTaskAttachmentTests
{
    [Fact]
    public async Task File_routes_require_every_scope_key_and_preserve_saved_revision_evidence()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context, null);
        context.TaskRequests.Add(TestDatabase.Task(2));
        context.TaskShares.Add(new TaskShare { TaskRequestId = 2, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        await context.SaveChangesAsync();
        var service = TaskCollaborationTestServices.SubTasks(context);
        var item = await StartDelegatedAsync(service, 2, "Evidence");
        var sibling = await StartDelegatedAsync(service, 2, "Sibling");
        item = Data(await service.SubmitAsync(1, item.Id, new() { Version = item.Version, Content = "Work" }, 2));
        var submissionId = item.CurrentSubmissionId!.Value;
        var store = new FileStore();
        using var stream = new MemoryStream("Supporting evidence"u8.ToArray());
        var file = new FormFile(stream, 0, stream.Length, "file", "notes.txt");

        Assert.IsType<ForbidResult>(await Controller(context, 3, store).ListSubTaskFiles(1, item.Id, default));
        Assert.IsType<NotFoundResult>(await Controller(context, 2, store)
            .UploadSubTaskFile(2, item.Id, submissionId, item.Version, file, default));
        Assert.IsType<ConflictObjectResult>(await Controller(context, 2, store)
            .UploadSubTaskFile(1, sibling.Id, submissionId, sibling.Version, file, default));
        Assert.IsType<ConflictObjectResult>(await Controller(context, 1, store)
            .UploadSubTaskFile(1, item.Id, submissionId, item.Version, file, default));
        Assert.Empty(store.Files);

        var saved = Assert.IsType<CreatedAtActionResult>(await Controller(context, 2, store)
            .UploadSubTaskFile(1, item.Id, submissionId, item.Version, file, default));
        var attachment = Assert.IsType<TaskAttachmentDto>(saved.Value);
        Assert.Equal(submissionId, attachment.SubTaskSubmissionId);
        Assert.False(attachment.CanDelete);
        var owner = Controller(context, 1, store);
        var listed = Assert.IsType<SubTaskFilesDto>(Assert.IsType<OkObjectResult>(
            await owner.ListSubTaskFiles(1, item.Id, default)).Value);
        Assert.Equal(item.Version + 1, listed.Version);
        Assert.Null(listed.UploadSubmissionId);
        Assert.Single(listed.Files);
        Assert.IsType<NotFoundResult>(await owner.DownloadSubTaskFile(1, sibling.Id, submissionId, attachment.Id, default));
        Assert.IsType<NotFoundResult>(await owner.DownloadSubTaskFile(2, item.Id, submissionId, attachment.Id, default));
        Assert.IsType<NotFoundResult>(await owner.DownloadSubTaskFile(1, item.Id, submissionId + 1, attachment.Id, default));
        Assert.IsType<ForbidResult>(await Controller(context, 3, store)
            .DownloadSubTaskFile(1, item.Id, submissionId, attachment.Id, default));
        var download = Assert.IsType<FileStreamResult>(await owner.DownloadSubTaskFile(1, item.Id, submissionId, attachment.Id, default));
        await using var data = download.FileStream;
        Assert.Equal("notes.txt", download.FileDownloadName);
        Assert.Equal("nosniff", owner.Response.Headers.XContentTypeOptions.ToString());
        Assert.IsType<ConflictObjectResult>(await Controller(context, 2, store)
            .UploadSubTaskFile(1, item.Id, submissionId, item.Version, file, default));
        Assert.IsAssignableFrom<IConflictResult>(await service.ReviewAsync(1, item.Id, submissionId, Review(item.Version), 1));
        Assert.True((await service.ReviewAsync(1, item.Id, submissionId, Review(listed.Version), 1)).Success);
        Assert.IsType<ConflictObjectResult>(await Controller(context, 2, store)
            .UploadSubTaskFile(1, item.Id, submissionId, listed.Version + 1, file, default));
        Assert.Single(await context.TaskAttachments.ToListAsync());
        Assert.Single(store.Files);
    }

    [Fact]
    public async Task File_upload_fails_closed_for_disabled_uploads_unavailable_scanning_and_invalid_files()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var item = await StartDelegatedAsync(service, 2, "File validation");
        item = Data(await service.SubmitAsync(1, item.Id, new() { Version = item.Version, Content = "Work" }, 2));
        var store = new FileStore();
        using var stream = new MemoryStream("Not a PDF"u8.ToArray());
        var file = new FormFile(stream, 0, stream.Length, "file", "notes.txt");
        async Task<IActionResult> Upload(TaskAttachmentController controller, IFormFile value) =>
            await controller.UploadSubTaskFile(1, item.Id, item.CurrentSubmissionId!.Value, item.Version, value, default);

        Assert.Equal(503, Assert.IsType<ObjectResult>(await Upload(Controller(context, 2, store, enabled: false), file)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await Upload(Controller(context, 2, store, new NoOpTaskFileScanner()), file)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await Upload(Controller(context, 2, store,
            new Scanner(TaskFileScanVerdict.NotConfigured)), file)).StatusCode);
        Assert.IsType<BadRequestObjectResult>(await Upload(Controller(context, 2, store, new Scanner(TaskFileScanVerdict.Rejected)), file));
        Assert.IsType<BadRequestObjectResult>(await Upload(Controller(context, 2, store),
            new FormFile(stream, 0, stream.Length, "file", "spoof.pdf")));
        Assert.Equal(413, Assert.IsType<ObjectResult>(await Upload(Controller(context, 2, store, maxSize: 1), file)).StatusCode);
        Assert.Empty(await context.TaskAttachments.ToListAsync());
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task Aborted_list_subtask_files_request_stops_before_queries_and_middleware_does_not_emit_500()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task(20));
        var subTask = new SubTask
        {
            Id = 2, TaskRequestId = 20, CreatedByUserId = 1,
            Title = "Subtask 2", Description = "Investigated subtask"
        };
        context.SubTasks.Add(subTask);
        await context.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var controller = Controller(context, 1, new FileStore());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.ListSubTaskFiles(20, 2, cts.Token));
        Assert.Null(context.Database.CurrentTransaction);

        var httpContext = new DefaultHttpContext { RequestAborted = cts.Token };
        httpContext.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(async ctx => await controller.ListSubTaskFiles(20, 2, ctx.RequestAborted));
        await middleware.InvokeAsync(httpContext);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, httpContext.Response.StatusCode);
        Assert.Equal(0, httpContext.Response.Body.Length);
    }

    [PostgreSqlFact]
    public async Task Concurrent_upload_and_review_using_the_same_child_version_have_one_winner()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        SubTaskDto item;
        await using (var seed = database.CreateContext())
        {
            await SeedPersonalAsync(seed);
            var service = TaskCollaborationTestServices.SubTasks(seed);
            item = await StartDelegatedAsync(service, 2, "Review race");
            item = Data(await service.SubmitAsync(1, item.Id, new() { Version = item.Version, Content = "Work" }, 2));
        }
        var barrier = new SaveBarrierInterceptor("UPDATE \"SubTasks\"");
        await using var uploadContext = database.CreateContext(barrier);
        await using var reviewContext = database.CreateContext(barrier);
        var store = new FileStore();
        using var content = new MemoryStream("Evidence"u8.ToArray());
        var submissionId = item.CurrentSubmissionId!.Value;
        var upload = Controller(uploadContext, 2, store)
            .UploadSubTaskFile(1, item.Id, submissionId, item.Version,
                new FormFile(content, 0, content.Length, "file", "work.txt"), default);
        var review = TaskCollaborationTestServices.SubTasks(reviewContext)
            .ReviewAsync(1, item.Id, submissionId, Review(item.Version), 1);
        await Task.WhenAll(upload, review).WaitAsync(TimeSpan.FromSeconds(25));
        var uploaded = upload.Result is CreatedAtActionResult;
        Assert.NotEqual(uploaded, review.Result.Success);
        if (uploaded) Assert.IsAssignableFrom<IConflictResult>(review.Result);
        else Assert.IsType<ConflictObjectResult>(upload.Result);
        await using var verify = database.CreateContext();
        Assert.Equal(uploaded ? 1 : 0, await verify.TaskAttachments.CountAsync());
        Assert.Equal(uploaded ? 1 : 0, store.Files.Count);
        Assert.Equal(uploaded ? 0 : 1, await verify.SubTaskSubmissionReviews.CountAsync());
    }

    [Fact]
    public async Task Atomic_subtask_deliveries_support_message_only_file_only_and_combined_revisions_with_immutable_history()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context, null);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var item = await StartDelegatedAsync(service, 2, "Multi-revision delivery");
        var store = new FileStore();

        // Revision 1: Message-only delivery works even when file uploads/scanner are disabled.
        var rev1Result = await Controller(context, 2, store, new NoOpTaskFileScanner(), enabled: false)
            .DeliverSubTaskSubmission(1, item.Id, DeliveryForm(item.Version, "Revision 1 summary", Guid.NewGuid()), default);
        var afterRev1 = Assert.IsType<SubTaskDto>(Assert.IsType<OkObjectResult>(rev1Result).Value);
        Assert.Equal(TaskStatus.InReview, afterRev1.Status);
        var rev1Id = afterRev1.CurrentSubmissionId!.Value;

        // Request changes on Revision 1.
        item = Data(await service.ReviewAsync(1, item.Id, rev1Id,
            Review(afterRev1.Version, TaskReviewDecision.ChangesRequested, "Attach the supporting files"), 1));
        Assert.Equal(TaskStatus.InProgress, item.Status);

        // Revision 2: File-only delivery (empty content + 1 file).
        using var fileA = CreateFile("evidence-v2.txt", "Revision 2 file payload");
        var rev2Result = await Controller(context, 2, store)
            .DeliverSubTaskSubmission(1, item.Id, DeliveryForm(item.Version, null, Guid.NewGuid(), fileA.Form), default);
        var afterRev2 = Assert.IsType<SubTaskDto>(Assert.IsType<OkObjectResult>(rev2Result).Value);
        Assert.Equal(TaskStatus.InReview, afterRev2.Status);
        var rev2Id = afterRev2.CurrentSubmissionId!.Value;
        Assert.NotEqual(rev1Id, rev2Id);

        // Request changes on Revision 2.
        item = Data(await service.ReviewAsync(1, item.Id, rev2Id,
            Review(afterRev2.Version, TaskReviewDecision.ChangesRequested, "Add a second file and summary"), 1));

        // Revision 3: Combined message + multiple files delivery.
        using var fileB = CreateFile("notes-v3.txt", "Revision 3 notes");
        using var fileC = CreateFile("data-v3.csv", "col1,col2\n1,2");
        var rev3Result = await Controller(context, 2, store)
            .DeliverSubTaskSubmission(1, item.Id,
                DeliveryForm(item.Version, "Revision 3 complete package", Guid.NewGuid(), fileB.Form, fileC.Form), default);
        var afterRev3 = Assert.IsType<SubTaskDto>(Assert.IsType<OkObjectResult>(rev3Result).Value);
        Assert.Equal(TaskStatus.InReview, afterRev3.Status);
        var rev3Id = afterRev3.CurrentSubmissionId!.Value;

        // Atomic deliveries never expose a post-submission UploadSubmissionId.
        var ownerController = Controller(context, 1, store);
        var filesDto = Assert.IsType<SubTaskFilesDto>(Assert.IsType<OkObjectResult>(
            await ownerController.ListSubTaskFiles(1, item.Id, default)).Value);
        Assert.Null(filesDto.UploadSubmissionId);
        Assert.Equal(3, filesDto.Files.Count);
        Assert.Single(filesDto.Files, f => f.SubTaskSubmissionId == rev2Id && f.FileName == "evidence-v2.txt");
        Assert.Equal(2, filesDto.Files.Count(f => f.SubTaskSubmissionId == rev3Id));

        // Approve Revision 3 and verify full immutable submission history.
        item = Data(await service.ReviewAsync(1, item.Id, rev3Id, Review(afterRev3.Version), 1));
        Assert.Equal(TaskStatus.Completed, item.Status);

        var history = Data(await service.GetSubmissionHistoryAsync(1, item.Id, 1));
        Assert.Equal([3, 2, 1], history.Select(h => h.RevisionNumber));
        Assert.Equal([2, 1, 0], history.Select(h => h.DeliveryFileCount));
        Assert.Equal("Revision 3 complete package", history[0].Content);
        Assert.Equal(string.Empty, history[1].Content);
        Assert.Equal("Revision 1 summary", history[2].Content);
    }

    [Fact]
    public async Task Failure_during_multi_file_staging_or_scan_never_marks_subtask_in_review_and_allows_idempotent_retry()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context, null);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var item = await StartDelegatedAsync(service, 2, "Atomic rollback and idempotency");
        var initialVersion = item.Version;

        using var first = CreateFile("first.txt", "first file");
        using var second = CreateFile("second.txt", "second file");
        using var spoofed = CreateFile("spoofed.pdf", "not a pdf");
        var requestId = Guid.NewGuid();

        // 1. Scanner rejection / invalid signature on second file leaves subtask InProgress and stores nothing.
        var healthyStore = new FileStore();
        var badSignature = await Controller(context, 2, healthyStore).DeliverSubTaskSubmission(
            1, item.Id, DeliveryForm(initialVersion, "With bad second file", Guid.NewGuid(), first.Form, spoofed.Form), default);
        Assert.IsType<BadRequestObjectResult>(badSignature);
        Assert.Equal(TaskStatus.InProgress, (await context.SubTasks.SingleAsync()).Status);
        Assert.Equal(initialVersion, (await context.SubTasks.SingleAsync()).Version);
        Assert.Empty(await context.SubTaskSubmissions.ToListAsync());
        Assert.Empty(await context.TaskAttachments.ToListAsync());
        Assert.Empty(healthyStore.Files);

        // 2. ClamAV scanner unavailable (503) or rejected (400) leaves subtask InProgress.
        var scanUnavailable = await Controller(context, 2, healthyStore, new Scanner(TaskFileScanVerdict.NotConfigured))
            .DeliverSubTaskSubmission(1, item.Id, DeliveryForm(initialVersion, "Scan unavailable", Guid.NewGuid(), first.Form), default);
        Assert.Equal(503, Assert.IsType<ObjectResult>(scanUnavailable).StatusCode);
        var scanRejected = await Controller(context, 2, healthyStore, new Scanner(TaskFileScanVerdict.Rejected))
            .DeliverSubTaskSubmission(1, item.Id, DeliveryForm(initialVersion, "Scan rejected", Guid.NewGuid(), first.Form), default);
        Assert.IsType<BadRequestObjectResult>(scanRejected);
        Assert.Equal(TaskStatus.InProgress, (await context.SubTasks.SingleAsync()).Status);
        Assert.Empty(healthyStore.Files);

        // 3. Storage failure on the second file rolls back the first staged file and keeps subtask InProgress.
        var failingStore = new FileStore { FailSave = 2 };
        var form = DeliveryForm(initialVersion, "Atomic batch", requestId, first.Form, second.Form);
        await Assert.ThrowsAsync<IOException>(() =>
            Controller(context, 2, failingStore).DeliverSubTaskSubmission(1, item.Id, form, default));
        Assert.Equal(TaskStatus.InProgress, (await context.SubTasks.SingleAsync()).Status);
        Assert.Equal(initialVersion, (await context.SubTasks.SingleAsync()).Version);
        Assert.Empty(await context.SubTaskSubmissions.ToListAsync());
        Assert.Empty(await context.TaskAttachments.ToListAsync());
        Assert.Empty(failingStore.Files);

        // 4. Retrying the exact same request succeeds atomically.
        var saved = Assert.IsType<SubTaskDto>(Assert.IsType<OkObjectResult>(
            await Controller(context, 2, healthyStore).DeliverSubTaskSubmission(1, item.Id, form, default)).Value);
        Assert.Equal(TaskStatus.InReview, saved.Status);
        Assert.Equal(initialVersion + 1, saved.Version);
        Assert.Single(await context.SubTaskSubmissions.ToListAsync());
        Assert.Equal(2, await context.TaskAttachments.CountAsync());
        Assert.Equal(2, healthyStore.Files.Count);

        // 5. Replaying the same ClientRequestId after commit (even with original Version and uploads disabled) returns the same receipt without duplicating.
        var replay = Assert.IsType<SubTaskDto>(Assert.IsType<OkObjectResult>(
            await Controller(context, 2, healthyStore, enabled: false).DeliverSubTaskSubmission(1, item.Id, form, default)).Value);
        Assert.Equal(saved.CurrentSubmissionId, replay.CurrentSubmissionId);
        Assert.Equal(saved.Version, replay.Version);
        Assert.Single(await context.SubTaskSubmissions.ToListAsync());
        Assert.Equal(2, await context.TaskAttachments.CountAsync());
        Assert.Equal(2, healthyStore.Files.Count);

        // 6. Reusing the same ClientRequestId with different payload returns 409 delivery_conflict.
        using var altered = CreateFile("second.txt", "altered content");
        var conflict = await Controller(context, 2, healthyStore).DeliverSubTaskSubmission(
            1, item.Id, DeliveryForm(initialVersion, "Atomic batch", requestId, first.Form, altered.Form), default);
        Assert.IsType<ConflictObjectResult>(conflict);
    }

    [Fact]
    public async Task Access_revocation_during_staging_cancels_subtask_delivery_and_cleans_up_staged_files()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context, null);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var item = await StartDelegatedAsync(service, 2, "Revocation during staging");
        var store = new FileStore
        {
            AfterSave = async () =>
            {
                await using var revocation = database.CreateContext();
                Assert.True((await TaskCollaborationTestServices.TaskShares(revocation, 1)
                    .RemoveParticipantAsync(1, 2, 0)).Success);
            }
        };

        using var file = CreateFile("evidence.txt", "staged before revocation");
        var result = await Controller(context, 2, store).DeliverSubTaskSubmission(
            1, item.Id, DeliveryForm(item.Version, "Work summary", Guid.NewGuid(), file.Form), default);
        Assert.IsType<ForbidResult>(result);

        await using var verify = database.CreateContext();
        var subTask = await verify.SubTasks.SingleAsync();
        Assert.Equal(TaskStatus.Pending, subTask.Status);
        Assert.Null(subTask.AssigneeUserId);
        Assert.Empty(await verify.SubTaskSubmissions.ToListAsync());
        Assert.Empty(await verify.TaskAttachments.ToListAsync());
        Assert.Empty(store.Files);
    }

    [PostgreSqlFact]
    public async Task Concurrent_identical_subtask_delivery_retries_commit_one_atomic_revision_and_file_set()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        SubTaskDto item;
        await using (var seed = database.CreateContext())
        {
            await SeedPersonalAsync(seed, null);
            item = await StartDelegatedAsync(TaskCollaborationTestServices.SubTasks(seed), 2, "Concurrent retry");
        }

        var bothStaged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        var store = new FileStore
        {
            AfterSave = async () =>
            {
                if (Interlocked.Increment(ref arrivals) == 2) bothStaged.TrySetResult();
                await bothStaged.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
        };

        var requestId = Guid.NewGuid();
        using var fileA = CreateFile("delivery.txt", "identical payload");
        using var fileB = CreateFile("delivery.txt", "identical payload");
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();

        var results = await Task.WhenAll(
            Controller(first, 2, store).DeliverSubTaskSubmission(1, item.Id,
                DeliveryForm(item.Version, "Concurrent retry summary", requestId, fileA.Form), default),
            Controller(second, 2, store).DeliverSubTaskSubmission(1, item.Id,
                DeliveryForm(item.Version, "Concurrent retry summary", requestId, fileB.Form), default))
            .WaitAsync(TimeSpan.FromSeconds(25));

        var dto1 = Assert.IsType<SubTaskDto>(Assert.IsType<OkObjectResult>(results[0]).Value);
        var dto2 = Assert.IsType<SubTaskDto>(Assert.IsType<OkObjectResult>(results[1]).Value);
        Assert.Equal(dto1.CurrentSubmissionId, dto2.CurrentSubmissionId);
        Assert.Equal(TaskStatus.InReview, dto1.Status);

        await using var verify = database.CreateContext();
        var submission = Assert.Single(await verify.SubTaskSubmissions.ToListAsync());
        Assert.Equal(1, submission.RevisionNumber);
        Assert.Equal(1, submission.DeliveryFileCount);
        Assert.Single(await verify.TaskAttachments.ToListAsync());
        Assert.Single(store.Files);
        Assert.Single(await verify.SubTaskActivities.Where(x => x.ActivityType == TaskActivityType.SubmissionCreated).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task PostgreSql_database_trigger_rejects_file_only_subtask_submission_without_its_attachments()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context, null);
        var item = await StartDelegatedAsync(TaskCollaborationTestServices.SubTasks(context), 2, "Trigger guard");

        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SubTaskSubmissions" ("TaskRequestId", "SubTaskId", "SubmittedByUserId", "RevisionNumber", "Content", "DeliveryFileCount", "ClientRequestId", "DeliveryFingerprint")
            VALUES (1, {item.Id}, 2, 1, '', 1, {Guid.NewGuid()}, {new string('b', 64)})
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Empty(await context.SubTaskSubmissions.ToListAsync());
    }

    private static TaskAttachmentController.SubTaskDeliveryForm DeliveryForm(long version, string? content, Guid id,
        params IFormFile[] files) =>
        new() { Version = version, ClientRequestId = id, Content = content, Files = files.ToList() };

    private static TestUploadFile CreateFile(string name, string content) => new(name, Encoding.UTF8.GetBytes(content));

    private sealed class TestUploadFile(string name, byte[] data) : IDisposable
    {
        private readonly MemoryStream stream = new(data);
        public IFormFile Form => new FormFile(stream, 0, stream.Length, "files", name);
        public void Dispose() => stream.Dispose();
    }

    private static TaskAttachmentController Controller(TaskTrackerDbContext context, int userId, FileStore store,
        ITaskFileScanner? scanner = null, bool enabled = true, long maxSize = 20 * 1024 * 1024) =>
        new(context, new CurrentUser(userId), TaskCollaborationTestServices.Create(context), store,
            scanner ?? new Scanner(TaskFileScanVerdict.Clean),
            Options.Create(new TaskFileOptions { EnableSubTaskSubmissionUploads = enabled, MaxFileSizeBytes = maxSize }),
            new EfSubTaskDal(context)) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };

    private sealed class CurrentUser(int userId) : ICurrentUserService { public int UserId => userId; }

    private sealed class Scanner(TaskFileScanVerdict verdict) : ITaskFileScanner
    {
        public Task<TaskFileScanVerdict> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(verdict);
    }

    private sealed class FileStore : ITaskFileStore
    {
        public ConcurrentDictionary<string, byte[]> Files { get; } = new();
        public int FailSave { get; init; }
        public Func<Task>? AfterSave { get; set; }
        private int saves;

        public async Task<string> SaveAsync(Stream source, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref saves) == FailSave) throw new IOException("Staging failed.");
            using var contents = new MemoryStream();
            await source.CopyToAsync(contents, cancellationToken);
            var key = Guid.NewGuid().ToString("N");
            Files[key] = contents.ToArray();
            if (AfterSave is not null) await AfterSave();
            return key;
        }
        public Stream OpenRead(string key) => new MemoryStream(Files[key]);
        public void Delete(string key) => Files.TryRemove(key, out _);
    }
}
