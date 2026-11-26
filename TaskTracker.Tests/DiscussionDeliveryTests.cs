using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TaskTracker.API.Controllers;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;
using static TaskTracker.Tests.SubTaskWorkflowTests;

namespace TaskTracker.Tests;

public class DiscussionDeliveryTests
{
    [Theory]
    [InlineData("Message text", 0)]
    [InlineData(null, 1)]
    [InlineData("Message and files", 2)]
    public async Task Message_deliveries_preserve_text_files_and_history_for_the_other_participant(string? content, int count)
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db, null);
        var store = new RecordingStore();
        var realtime = new Realtime
        {
            OnMessage = async (taskId, message) =>
            {
                Assert.Equal(1, taskId);
                Assert.Null(db.Database.CurrentTransaction);
                await using var verify = database.CreateContext();
                Assert.True(await verify.TaskMessages.AnyAsync(x => x.Id == message.Id));
                Assert.Equal(count, await verify.TaskAttachments.CountAsync(x => x.TaskMessageId == message.Id));
            }
        };
        var sender = Controller(db, 1, store, realtime: realtime);
        using var first = File("Evidence İ.txt", "first");
        using var second = File("Notes.csv", "header,value");
        var send = Form(content, Guid.NewGuid(), new[] { first.Form, second.Form }.Take(count).ToArray());
        var message = Receipt(await sender.DeliverDiscussionMessage(1, send, default));
        Assert.Equal(content ?? "", message.Content);
        Assert.Equal(count, message.DeliveryFileCount);
        Assert.Single(realtime.Messages);
        Assert.Equal(count == 0 ? 0 : 1, realtime.AttachmentUpdates);

        await using var reader = database.CreateContext();
        var conversation = TaskCollaborationTestServices.Create(reader);
        Assert.Equal(message.Id, Assert.Single(Data(await conversation.GetMessagesAsync(1, 2))).Id);
        var participant = Controller(reader, 2, store);
        var files = Assert.IsAssignableFrom<IEnumerable<TaskAttachmentDto>>(Assert.IsType<OkObjectResult>(
            await participant.List(1)).Value).ToArray();
        Assert.Equal(count, files.Length);
        Assert.All(files, file => Assert.Equal(message.Id, file.TaskMessageId));
        if (count > 0)
        {
            Assert.Equal("Evidence İ.txt", files[0].FileName);
            var result = Assert.IsType<FileStreamResult>(await participant.Download(files[0].Id));
            await using var download = result.FileStream;
            using var text = new StreamReader(download);
            Assert.Equal("first", await text.ReadToEndAsync());
            Assert.Equal("application/octet-stream", result.ContentType);
            Assert.IsType<ForbidResult>(await Controller(reader, 3, store).Download(files[0].Id));
        }
    }

    [Fact]
    public async Task Failure_on_the_second_file_rolls_back_the_batch_and_the_same_delivery_can_be_retried()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db);
        var realtime = new Realtime();
        var store = new RecordingStore { FailSave = 2 };
        var controller = Controller(db, 1, store, realtime: realtime);
        using var first = File("first.txt", "first");
        using var second = File("second.txt", "second");
        var request = Form(null, Guid.NewGuid(), first.Form, second.Form);

        await Assert.ThrowsAsync<IOException>(() => controller.DeliverDiscussionMessage(1, request, default));
        Assert.Empty(await db.TaskMessages.ToListAsync());
        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(store.Files);
        Assert.Empty(realtime.Messages);

        var saved = Receipt(await controller.DeliverDiscussionMessage(1, request, default));
        var replay = Receipt(await Controller(db, 1, store, options: new() { EnableDiscussionUploads = false })
            .DeliverDiscussionMessage(1, request, default));
        Assert.Equal(saved.Id, replay.Id);
        Assert.Single(await db.TaskMessages.ToListAsync());
        Assert.Equal(2, await db.TaskAttachments.CountAsync());
        Assert.Equal(2, store.Files.Count);
        Assert.Single(realtime.Messages);
        using var changed = File("second.txt", "seconX");
        Assert.IsType<ConflictObjectResult>(await controller.DeliverDiscussionMessage(1,
            Form(null, request.ClientRequestId, first.Form, changed.Form), default));
    }

    [Fact]
    public async Task Invalid_disabled_and_unauthorized_deliveries_persist_nothing_and_text_remains_available()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db, null);
        var store = new RecordingStore();
        using var good = File("notes.txt", "12345");
        using var other = File("other.txt", "67890");
        using var wrong = File("blocked.exe", "text");
        using var spoofed = File("not-an-image.png", "text");
        var send = Form(null, Guid.NewGuid(), good.Form);
        var controller = Controller(db, 1, store);
        Assert.IsType<ForbidResult>(await Controller(db, 3, store).DeliverDiscussionMessage(1, send, default));
        Assert.IsType<BadRequestObjectResult>(await controller.DeliverDiscussionMessage(1, Form(" \t", Guid.NewGuid()), default));
        Assert.IsType<BadRequestObjectResult>(await controller.DeliverDiscussionMessage(1, Form("", Guid.NewGuid(), wrong.Form), default));
        Assert.IsType<BadRequestObjectResult>(await controller.DeliverDiscussionMessage(1, Form("", Guid.NewGuid(), spoofed.Form), default));
        Assert.IsType<BadRequestObjectResult>(await controller.DeliverDiscussionMessage(1, Form("", Guid.NewGuid(), good.Form, good.Form), default));
        Assert.Equal(413, Status(await Controller(db, 1, store, options: new() { MaxFileSizeBytes = 4 })
            .DeliverDiscussionMessage(1, send, default)));
        Assert.Equal(413, Status(await Controller(db, 1, store, options: new() { MaxDiscussionUploadBytes = 8 })
            .DeliverDiscussionMessage(1, Form(null, Guid.NewGuid(), good.Form, other.Form), default)));
        Assert.IsType<BadRequestObjectResult>(await Controller(db, 1, store, options: new() { MaxDiscussionAttachments = 1 })
            .DeliverDiscussionMessage(1, Form(null, Guid.NewGuid(), good.Form, other.Form), default));
        Assert.Equal(503, Status(await Controller(db, 1, store, scanner: new NoOpTaskFileScanner())
            .DeliverDiscussionMessage(1, send, default)));
        Assert.Equal(503, Status(await Controller(db, 1, store, scanner: new Scanner(TaskFileScanVerdict.NotConfigured))
            .DeliverDiscussionMessage(1, send, default)));
        Assert.Equal(503, Status(await Controller(db, 1, store, options: new() { EnableDiscussionUploads = false })
            .DeliverDiscussionMessage(1, send, default)));
        Assert.IsType<BadRequestObjectResult>(await Controller(db, 1, store, scanner: new Scanner(TaskFileScanVerdict.Rejected))
            .DeliverDiscussionMessage(1, send, default));
        Assert.Empty(await db.TaskMessages.ToListAsync());
        Assert.Empty(store.Files);
        Assert.Equal("Text works", Receipt(await Controller(db, 1, store, scanner: new NoOpTaskFileScanner())
            .DeliverDiscussionMessage(1, Form("Text works", Guid.NewGuid()), default)).Content);
    }

    [Fact]
    public async Task Revocation_during_staging_cancels_delivery_and_blocks_download_of_previous_files()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db, null);
        var store = new RecordingStore();
        using var file = File("notes.txt", "private evidence");
        var form = Form(null, Guid.NewGuid(), file.Form);
        var owner = Controller(db, 1, store);
        var saved = Receipt(await owner.DeliverDiscussionMessage(1, form, default));
        var attachment = await db.TaskAttachments.SingleAsync();
        Assert.Equal(saved.Id, attachment.TaskMessageId);
        Assert.IsType<BadRequestObjectResult>(await Controller(db, 2, store).UploadMessage(1, saved.Id, file.Form, default));

        store.AfterSave = async () =>
        {
            await using var revocation = database.CreateContext();
            Assert.True((await TaskCollaborationTestServices.TaskShares(revocation, 1).RemoveParticipantAsync(1, 2, 0)).Success);
        };
        var former = Controller(db, 2, store);
        Assert.IsType<ForbidResult>(await former.DeliverDiscussionMessage(1, form, default));
        Assert.IsType<ForbidResult>(await former.Download(attachment.Id));
        Assert.Single(await db.TaskMessages.ToListAsync());
        Assert.Single(await db.TaskAttachments.ToListAsync());
        Assert.Single(store.Files);
    }

    [Fact]
    public async Task Existing_single_file_upload_can_be_replayed_without_creating_another_attachment()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db, null);
        var message = Data(await TaskCollaborationTestServices.Create(db).SendMessageAsync(1, 2, "Legacy message"));
        var store = new RecordingStore();
        var controller = Controller(db, 2, store);
        using var file = File("legacy.txt", "file");
        Assert.IsType<ForbidResult>(await Controller(db, 3, store).UploadMessage(1, message.Id, file.Form, default));
        Assert.Equal(503, Status(await Controller(db, 2, store, scanner: new NoOpTaskFileScanner())
            .UploadMessage(1, message.Id, file.Form, default)));
        var original = Assert.IsType<TaskAttachmentDto>(Assert.IsType<CreatedAtActionResult>(
            await controller.UploadMessage(1, message.Id, file.Form, default)).Value);
        var replay = Assert.IsType<TaskAttachmentDto>(Assert.IsType<OkObjectResult>(
            await controller.UploadMessage(1, message.Id, file.Form, default)).Value);
        Assert.Equal(original.Id, replay.Id);
        Assert.Single(await db.TaskAttachments.ToListAsync());
        Assert.Single(store.Files);
    }

    [PostgreSqlFact]
    public async Task Concurrent_identical_retries_publish_one_atomic_delivery()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seed = database.CreateContext()) await SeedPersonalAsync(seed, null);
        var bothStaged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        var store = new RecordingStore { AfterSave = async () =>
        {
            if (Interlocked.Increment(ref arrivals) == 2) bothStaged.TrySetResult();
            await bothStaged.Task.WaitAsync(TimeSpan.FromSeconds(15));
        } };
        var realtime = new Realtime();
        var requestId = Guid.NewGuid();
        using var a = File("file.txt", "identical");
        using var b = File("file.txt", "identical");
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var results = await Task.WhenAll(
            Controller(first, 1, store, realtime: realtime).DeliverDiscussionMessage(1, Form(null, requestId, a.Form), default),
            Controller(second, 1, store, realtime: realtime).DeliverDiscussionMessage(1, Form(null, requestId, b.Form), default))
            .WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Equal(Receipt(results[0]).Id, Receipt(results[1]).Id);
        await using var verify = database.CreateContext();
        Assert.Single(await verify.TaskMessages.ToListAsync());
        Assert.Single(await verify.TaskAttachments.ToListAsync());
        Assert.Single(store.Files);
        Assert.Single(realtime.Messages);
        Assert.Equal(1, realtime.AttachmentUpdates);
    }

    [PostgreSqlFact]
    public async Task Database_rejects_a_file_only_delivery_without_its_files()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db, null);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TaskMessages" ("TaskRequestId", "SenderUserId", "Content", "DeliveryFileCount", "ClientRequestId", "DeliveryFingerprint")
            VALUES (1, 1, '', 1, {Guid.NewGuid()}, {new string('a', 64)})
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Empty(await db.TaskMessages.ToListAsync());
    }

    private static TaskAttachmentController Controller(TaskTrackerDbContext db, int userId, RecordingStore store,
        TaskFileOptions? options = null, ITaskFileScanner? scanner = null, Realtime? realtime = null)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "User")], "test"))
        };
        var conversation = new TaskCollaborationManager(new UnitOfWork(db), new EfTaskCollaborationDal(db),
            realtime ?? new Realtime(), NullLogger<TaskCollaborationManager>.Instance, new EfSubTaskDal(db));
        return new(db, new Actor(userId), conversation, store, scanner ?? new Scanner(TaskFileScanVerdict.Clean),
            Options.Create(options ?? new TaskFileOptions()), new EfSubTaskDal(db))
        { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static TaskAttachmentController.DiscussionDeliveryForm Form(string? content, Guid id, params IFormFile[] files) =>
        new() { ClientRequestId = id, Content = content, Files = files.ToList() };

    private static TaskMessageDto Receipt(IActionResult result) =>
        Assert.IsType<TaskMessageDto>(Assert.IsType<OkObjectResult>(result).Value);

    private static int? Status(IActionResult result) => Assert.IsType<ObjectResult>(result).StatusCode;

    private static TestFile File(string name, string content) => new(name, Encoding.UTF8.GetBytes(content));

    private sealed class TestFile(string name, byte[] data) : IDisposable
    {
        private readonly MemoryStream stream = new(data);
        public IFormFile Form => new FormFile(stream, 0, stream.Length, "files", name);
        public void Dispose() => stream.Dispose();
    }

    private sealed class Actor(int userId) : ICurrentUserService { public int UserId => userId; }

    private sealed class Scanner(TaskFileScanVerdict verdict) : ITaskFileScanner
    {
        public Task<TaskFileScanVerdict> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
            Task.FromResult(verdict);
    }

    private sealed class RecordingStore : ITaskFileStore
    {
        public ConcurrentDictionary<string, byte[]> Files { get; } = new();
        public int FailSave { get; init; }
        public Func<Task>? AfterSave { get; set; }
        private int saves;

        public async Task<string> SaveAsync(Stream source, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref saves) == FailSave) throw new IOException("Staging failed.");
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            var key = Guid.NewGuid().ToString("N");
            Files[key] = buffer.ToArray();
            if (AfterSave is not null) await AfterSave();
            return key;
        }
        public Stream OpenRead(string key) => new MemoryStream(Files[key]);
        public void Delete(string key) => Files.TryRemove(key, out _);
    }

    private sealed class Realtime : ITaskCollaborationRealtimeService
    {
        public ConcurrentBag<TaskMessageDto> Messages { get; } = [];
        public int AttachmentUpdates { get; private set; }
        public Func<int, TaskMessageDto, Task>? OnMessage { get; init; }
        public async Task MessageCreatedAsync(int taskId, TaskMessageDto message)
        {
            if (OnMessage is not null) await OnMessage(taskId, message);
            Messages.Add(message);
        }
        public Task AttachmentsChangedAsync(int taskId) { AttachmentUpdates++; return Task.CompletedTask; }
        public Task ActivityCreatedAsync(int taskId, TaskActivityDto activity) => Task.CompletedTask;
        public Task TaskChangedAsync(int taskId) => Task.CompletedTask;
        public Task WorkspaceRosterChangedAsync(int workspaceId, IReadOnlyList<int> taskIds) => Task.CompletedTask;
        public Task AccessRevokedAsync(int taskId, int userId) => Task.CompletedTask;
    }
}
