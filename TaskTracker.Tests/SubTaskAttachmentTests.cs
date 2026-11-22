using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
        public Dictionary<string, byte[]> Files { get; } = [];
        public async Task<string> SaveAsync(Stream source, CancellationToken cancellationToken)
        {
            using var contents = new MemoryStream();
            await source.CopyToAsync(contents, cancellationToken);
            var key = Guid.NewGuid().ToString("N");
            Files.Add(key, contents.ToArray());
            return key;
        }
        public Stream OpenRead(string key) => new MemoryStream(Files[key]);
        public void Delete(string key) => Files.Remove(key);
    }
}
