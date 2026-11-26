using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using TaskTracker.API.Controllers;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;
using static TaskTracker.Tests.SubTaskWorkflowTests;

namespace TaskTracker.Tests;

public sealed class PostgreSqlUploadSecurityTests
{
    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_subtask_quota_is_atomic_and_retries_do_not_consume_the_last_slot(bool byteQuota)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        SubTaskDto item;
        await using (var seed = database.CreateContext())
        {
            await SeedPersonalAsync(seed);
            var service = TaskCollaborationTestServices.SubTasks(seed);
            item = await StartDelegatedAsync(service, 2, "Legacy delivery");
            item = Data(await service.SubmitAsync(1, item.Id, new() { Version = item.Version, Content = "Review work" }, 2));
        }

        var options = new TaskFileOptions
        {
            MaxSubTaskSubmissionAttachments = byteQuota ? 5 : 2,
            MaxSubTaskSubmissionUploadBytes = byteQuota ? 8 : 40
        };
        var store = new RecordingStore();
        var submission = item.CurrentSubmissionId!.Value;
        Task<IActionResult> Upload(TaskTrackerDbContext db, string name, string content, long version, ITaskFileScanner? scanner = null) =>
            WithFile(name, content, file => Controller(db, 2, store, scanner, options)
                .UploadSubTaskFile(1, item.Id, submission, version, file, default));

        await using var baseUpload = database.CreateContext();
        Assert.IsType<CreatedAtActionResult>(await Upload(baseUpload, "initial.txt", "AAAA", item.Version));
        var barrier = new ScanBarrier();
        await using var left = database.CreateContext();
        await using var right = database.CreateContext();
        var results = await Task.WhenAll(
            Upload(left, "left.txt", "BBBB", item.Version + 1, barrier.Scanner(left)),
            Upload(right, "right.txt", "CCCC", item.Version + 1, barrier.Scanner(right)));
        var firstWon = results[0] is CreatedAtActionResult;
        var winner = Assert.IsType<CreatedAtActionResult>(results[firstWon ? 0 : 1]);
        Assert.IsType<ConflictObjectResult>(results[firstWon ? 1 : 0]);

        await using var retry = database.CreateContext();
        var repeat = Assert.IsType<OkObjectResult>(await Upload(retry, firstWon ? "left.txt" : "right.txt",
            firstWon ? "BBBB" : "CCCC", item.Version + 1, new Scanner(_ => throw new InvalidOperationException("Replay must not scan."))));
        Assert.Equal(Assert.IsType<TaskAttachmentDto>(winner.Value).Id, Assert.IsType<TaskAttachmentDto>(repeat.Value).Id);
        var excess = Assert.IsAssignableFrom<ObjectResult>(await Upload(retry, "excess.txt", "DDDD", item.Version + 2));
        Assert.Equal(byteQuota ? 413 : 400, excess.StatusCode);

        await using var verify = database.CreateContext();
        Assert.Equal(item.Version + 2, (await verify.SubTasks.SingleAsync()).Version);
        Assert.Equal(2, await verify.TaskAttachments.CountAsync());
        Assert.Equal(2, store.Files.Count);
        Assert.Equal(2, store.Deleted);
    }

    [PostgreSqlFact]
    public async Task Concurrent_reference_retries_commit_one_file_and_same_size_different_content_does_not_match()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seed = database.CreateContext()) await SeedPersonalAsync(seed);
        var store = new RecordingStore();
        var barrier = new ScanBarrier();
        await using var left = database.CreateContext();
        await using var right = database.CreateContext();
        Task<IActionResult> Upload(TaskTrackerDbContext db, int user, string text, ITaskFileScanner? scanner = null) =>
            WithFile("same.txt", text, file => Controller(db, user, store, scanner).UploadTask(1, file, default));
        var results = await Task.WhenAll(Upload(left, 1, "AAAA", barrier.Scanner(left)),
            Upload(right, 1, "AAAA", barrier.Scanner(right)));
        var created = Assert.Single(results.OfType<CreatedAtActionResult>());
        var replay = Assert.Single(results.OfType<OkObjectResult>());
        Assert.Equal(Assert.IsType<TaskAttachmentDto>(created.Value).Id, Assert.IsType<TaskAttachmentDto>(replay.Value).Id);
        Assert.Single(store.Files);
        Assert.Equal(1, store.Deleted);

        await using var different = database.CreateContext();
        Assert.IsType<CreatedAtActionResult>(await Upload(different, 1, "BBBB"));
        Assert.IsType<CreatedAtActionResult>(await Upload(different, 2, "AAAA"));
        Assert.Equal(3, await different.TaskAttachments.CountAsync());
    }

    [PostgreSqlTheory]
    [InlineData(false, "reference")]
    [InlineData(true, "reference")]
    [InlineData(false, "discussion-file")]
    [InlineData(true, "discussion-file")]
    [InlineData(false, "text")]
    [InlineData(true, "text")]
    public async Task Legacy_writes_wait_for_revocation_and_are_denied_after_it_commits(bool workspace, string flow)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        int? workspaceId = null;
        int messageId = 0;
        await using (var seed = database.CreateContext())
        {
            if (workspace) workspaceId = await SeedWorkspaceAsync(seed);
            else await SeedPersonalAsync(seed);
            if (flow == "discussion-file")
                messageId = Data(await TaskCollaborationTestServices.Create(seed).SendMessageAsync(1, 2, "Existing message")).Id;
        }

        var revokeGate = new PostgreSqlCommandGate(
            workspace ? "UPDATE \"WorkspaceMembers\"" : "DELETE FROM \"TaskShares\"", pause: true);
        var writeGate = new PostgreSqlCommandGate(
            workspace ? "FOR SHARE OF w" : flow == "text" ? "FOR SHARE" : "FOR NO KEY UPDATE", pause: false);
        await using var revocation = database.CreateContext(revokeGate);
        await using var writer = database.CreateContext(writeGate);
        var store = new RecordingStore();
        var scanner = new Scanner(_ => { Assert.Null(writer.Database.CurrentTransaction); return Task.CompletedTask; });
        var revoked = workspace
            ? TaskCollaborationTestServices.Workspaces(revocation, 1).RemoveMemberAsync(workspaceId!.Value, 2, new() { Version = 0 })
            : TaskCollaborationTestServices.TaskShares(revocation, 1).RemoveParticipantAsync(1, 2, 0);

        async Task AssertDeniedAsync()
        {
            if (flow == "text")
            {
                var result = await TaskCollaborationTestServices.Create(writer).SendMessageAsync(1, 2, "Must not be saved");
                Assert.False(result.Success);
                Assert.Equal(Messages.AuthorizationDenied, result.Message);
            }
            else
            {
                var controller = Controller(writer, 2, store, scanner);
                var result = await WithFile("blocked.txt", "Rejected", file => flow == "reference"
                    ? controller.UploadTask(1, file, default)
                    : controller.UploadMessage(1, messageId, file, default));
                Assert.IsType<ForbidResult>(result);
            }
        }

        Task write;
        try
        {
            await revokeGate.Arrived;
            write = AssertDeniedAsync();
            await writeGate.Arrived;
        }
        finally
        {
            revokeGate.Release();
        }
        Assert.True((await revoked).Success);
        await write;
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.TaskAttachments.ToListAsync());
        Assert.Equal(flow == "discussion-file" ? 1 : 0, await verify.TaskMessages.CountAsync());
        Assert.Empty(store.Files);
        Assert.Equal(flow == "text" ? 0 : 1, store.Deleted);
    }

    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_failure_checks_durable_references_before_removing_files(bool committed)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seed = database.CreateContext()) await SeedPersonalAsync(seed);
        await using var writer = database.CreateContext(new CommitFailure(committed));
        var store = new RecordingStore();
        await Assert.ThrowsAsync<IOException>(() =>
            WithFile("commit.txt", "Test", file => Controller(writer, 1, store).UploadTask(1, file, default)));
        await using var verify = database.CreateContext();
        Assert.Equal(committed ? 1 : 0, await verify.TaskAttachments.CountAsync());
        Assert.Equal(committed ? 1 : 0, store.Files.Count);
        Assert.Equal(committed ? 0 : 1, store.Deleted);
    }

    private static TaskAttachmentController Controller(TaskTrackerDbContext db, int userId, ITaskFileStore store,
        ITaskFileScanner? scanner = null, TaskFileOptions? options = null)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "User")], "test"))
        };
        return new(db, new Actor(userId), TaskCollaborationTestServices.Create(db), store,
            scanner ?? new Scanner(_ => Task.CompletedTask), Options.Create(options ?? new TaskFileOptions()), new EfSubTaskDal(db))
        { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static async Task<IActionResult> WithFile(string name, string content, Func<IFormFile, Task<IActionResult>> upload)
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await upload(new FormFile(source, 0, source.Length, "file", name));
    }

    private sealed class Actor(int id) : ICurrentUserService { public int UserId => id; }

    private sealed class Scanner(Func<CancellationToken, Task> beforeResult) : ITaskFileScanner
    {
        public async Task<TaskFileScanVerdict> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
        {
            await beforeResult(cancellationToken);
            return TaskFileScanVerdict.Clean;
        }
    }

    private sealed class ScanBarrier
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int scans;

        public ITaskFileScanner Scanner(TaskTrackerDbContext db) => new Scanner(async ct =>
        {
            Assert.Null(db.Database.CurrentTransaction);
            if (Interlocked.Increment(ref scans) == 2) entered.TrySetResult();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        });
    }

    private sealed class RecordingStore : ITaskFileStore
    {
        public ConcurrentDictionary<string, byte[]> Files { get; } = new();
        private int deleted;
        public int Deleted => Volatile.Read(ref deleted);

        public async Task<string> SaveAsync(Stream source, CancellationToken cancellationToken)
        {
            await using var destination = new MemoryStream();
            await source.CopyToAsync(destination, cancellationToken);
            var key = Guid.NewGuid().ToString("N");
            Files[key] = destination.ToArray();
            return key;
        }

        public Stream OpenRead(string key) => new MemoryStream(Files[key]);
        public void Delete(string key)
        {
            if (Files.TryRemove(key, out _)) Interlocked.Increment(ref deleted);
        }
    }

    private sealed class CommitFailure(bool committed) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            committed ? ValueTask.FromResult(result) : ValueTask.FromException<InterceptionResult>(new IOException("Commit failed."));

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) =>
            committed ? Task.FromException(new IOException("Commit acknowledgement lost.")) : Task.CompletedTask;
    }
}
