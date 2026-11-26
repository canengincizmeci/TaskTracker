using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskTracker.API.Controllers;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;
using static TaskTracker.Tests.SubTaskWorkflowTests;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public sealed class ClamAvTaskFileScannerTests
{
    [Fact]
    public async Task Private_store_removes_a_partial_file_when_copy_is_cancelled()
    {
        using var temp = new TempFileStore();
        using var cancellation = new CancellationTokenSource();
        await using var source = new CancelAfterWriteStream(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => temp.Store.SaveAsync(source, cancellation.Token));
        Assert.Empty(temp.DiskFiles());
    }

    private sealed class CancelAfterWriteStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await destination.WriteAsync("Partial file"u8.ToArray(), cancellationToken);
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    [Fact]
    public async Task Clean_multichunk_stream_and_partial_tcp_reads_are_accepted_and_downloadable_only_after_persistence()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db);
        using var storage = new TempFileStore();
        using var server = new FakeClamdServer(async (session, ct) =>
        {
            var payload = await session.ReadInstreamPayloadAsync(ct);
            Assert.True(session.ChunkCount >= 3, "Expected multi-chunk zINSTREAM transfer.");
            Assert.Equal(3200, payload.Length);
            // Verified during scan: no file exists on disk and no attachment row exists in DB before Clean verdict.
            Assert.Empty(storage.DiskFiles());
            // Send "stream: OK\0" in 1-byte fragments to test partial network reads.
            foreach (var b in "stream: OK\0"u8.ToArray())
            {
                await session.Stream.WriteAsync(new[] { b }, ct);
                await session.Stream.FlushAsync(ct);
            }
        });

        using var scanner = CreateScanner(server.Port, chunkSizeBytes: 1024);
        Assert.True(await scanner.PingAsync());

        var controller = CreateController(db, 1, storage.Store, scanner, server.Port);
        var bytes = Encoding.UTF8.GetBytes(new string('A', 3200));
        using var fileStream = new MemoryStream(bytes);
        var formFile = new FormFile(fileStream, 0, fileStream.Length, "file", "report.txt");

        var created = Assert.IsType<CreatedAtActionResult>(
            await controller.UploadTask(1, formFile, CancellationToken.None));
        var dto = Assert.IsType<TaskAttachmentDto>(created.Value);
        Assert.Equal("report.txt", dto.FileName);
        Assert.Equal(3200, dto.SizeBytes);
        Assert.Single(storage.DiskFiles());

        var download = Assert.IsType<FileStreamResult>(await controller.Download(dto.Id));
        await using var downloadedStream = download.FileStream;
        using var reader = new StreamReader(downloadedStream, Encoding.UTF8);
        Assert.Equal(bytes.Length, (await reader.ReadToEndAsync()).Length);
    }

    [Fact]
    public async Task Infected_files_are_rejected_and_never_stored_across_all_attachment_endpoints()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        var subTask = await SeedAllUploadContextsAsync(db);
        using var storage = new TempFileStore();
        using var server = new FakeClamdServer(async (session, ct) =>
        {
            var payload = Encoding.UTF8.GetString(await session.ReadInstreamPayloadAsync(ct));
            var reply = payload.Contains("INFECTED_MARKER", StringComparison.Ordinal)
                ? "stream: Win.Test.EICAR_HDB-1 FOUND\0"
                : "stream: OK\0";
            await session.WriteAsciiAsync(reply, ct);
        });

        // Even if RequireCleanScan is set to false in options, a configured scanner enforces Clean on all endpoints.
        var options = CreateOptions(server.Port, requireCleanScan: false);
        using var scanner = new ClamAvTaskFileScanner(Options.Create(options));
        var ownerController = CreateController(db, 1, storage.Store, scanner, server.Port, options);
        var assigneeController = CreateController(db, 2, storage.Store, scanner, server.Port, options);

        // 1. Task attachment endpoint
        Assert.IsType<BadRequestObjectResult>(
            await ownerController.UploadTask(1, MakeTextFile("infected-task.txt", "INFECTED_MARKER"), default));

        // 2. Parent Task submission attachment endpoint
        Assert.IsType<BadRequestObjectResult>(
            await assigneeController.UploadSubmission(2, 1, MakeTextFile("infected-submission.txt", "INFECTED_MARKER"), default));

        // 3. Legacy discussion message attachment endpoint
        Assert.IsType<BadRequestObjectResult>(
            await ownerController.UploadMessage(1, 1, MakeTextFile("infected-legacy.txt", "INFECTED_MARKER"), default));

        // 4. Atomic discussion message delivery endpoint
        var deliveryForm = new TaskAttachmentController.DiscussionDeliveryForm
        {
            ClientRequestId = Guid.NewGuid(),
            Content = "Message with infected file",
            Files = [MakeTextFile("infected-discussion.txt", "INFECTED_MARKER")]
        };
        Assert.IsType<BadRequestObjectResult>(
            await ownerController.DeliverDiscussionMessage(1, deliveryForm, default));

        // 5. SubTask submission attachment endpoint
        Assert.IsType<BadRequestObjectResult>(
            await assigneeController.UploadSubTaskFile(
                1, subTask.Id, subTask.CurrentSubmissionId!.Value, subTask.Version,
                MakeTextFile("infected-subtask.txt", "INFECTED_MARKER"), default));

        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(storage.DiskFiles());
    }

    [Fact]
    public async Task Unavailable_scanner_fails_closed_with_503_across_all_attachment_endpoints()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        var subTask = await SeedAllUploadContextsAsync(db);
        using var storage = new TempFileStore();
        var unusedPort = GetUnusedLoopbackPort();
        var options = CreateOptions(unusedPort, requireCleanScan: false);
        using var scanner = new ClamAvTaskFileScanner(Options.Create(options));
        Assert.False(await scanner.PingAsync());

        var ownerController = CreateController(db, 1, storage.Store, scanner, unusedPort, options);
        var assigneeController = CreateController(db, 2, storage.Store, scanner, unusedPort, options);

        Assert.Equal(503, StatusCode(await ownerController.UploadTask(1, MakeTextFile("a.txt", "clean"), default)));
        Assert.Equal(503, StatusCode(await assigneeController.UploadSubmission(2, 1, MakeTextFile("b.txt", "clean"), default)));
        Assert.Equal(503, StatusCode(await ownerController.UploadMessage(1, 1, MakeTextFile("c.txt", "clean"), default)));

        var deliveryForm = new TaskAttachmentController.DiscussionDeliveryForm
        {
            ClientRequestId = Guid.NewGuid(),
            Content = "Unavailable scan",
            Files = [MakeTextFile("d.txt", "clean")]
        };
        Assert.Equal(503, StatusCode(await ownerController.DeliverDiscussionMessage(1, deliveryForm, default)));

        Assert.Equal(503, StatusCode(await assigneeController.UploadSubTaskFile(
            1, subTask.Id, subTask.CurrentSubmissionId!.Value, subTask.Version,
            MakeTextFile("e.txt", "clean"), default)));

        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(storage.DiskFiles());
    }

    [Fact]
    public async Task Scanner_timeout_fails_closed_without_throwing_unhandled_exceptions()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db);
        using var storage = new TempFileStore();
        using var server = new FakeClamdServer(async (session, ct) =>
        {
            _ = await session.ReadInstreamPayloadAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        });

        using var scanner = CreateScanner(server.Port, scanTimeoutMs: 200);
        var controller = CreateController(db, 1, storage.Store, scanner, server.Port);
        var response = await controller.UploadTask(1, MakeTextFile("slow.txt", "content"), CancellationToken.None);

        Assert.Equal(503, StatusCode(response));
        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(storage.DiskFiles());
    }

    [Theory]
    [InlineData("UNKNOWN COMMAND\0")]
    [InlineData("stream: INSTREAM size limit exceeded. ERROR\0")]
    [InlineData("stream: FOUND\0")]
    [InlineData("stream:    FOUND\0")]
    [InlineData("OK\0")]
    [InlineData("PONG\0")]
    [InlineData("stream: OK\0EXTRA_FRAME")]
    [InlineData("\0")]
    public async Task Malformed_or_error_clamd_replies_are_treated_as_non_clean_and_fail_closed(string rawReply)
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db);
        using var storage = new TempFileStore();
        using var server = new FakeClamdServer(async (session, ct) =>
        {
            _ = await session.ReadInstreamPayloadAsync(ct);
            await session.WriteAsciiAsync(rawReply, ct);
        });

        using var scanner = CreateScanner(server.Port);
        var controller = CreateController(db, 1, storage.Store, scanner, server.Port);
        var result = await controller.UploadTask(1, MakeTextFile("test.txt", "hello world"), CancellationToken.None);

        Assert.Equal(503, StatusCode(result));
        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(storage.DiskFiles());
    }

    [Fact]
    public async Task Oversized_unterminated_clamd_reply_is_rejected_as_malformed()
    {
        using var server = new FakeClamdServer(async (session, ct) =>
        {
            _ = await session.ReadInstreamPayloadAsync(ct);
            var garbage = new byte[6000];
            Array.Fill(garbage, (byte)'X');
            await session.Stream.WriteAsync(garbage, ct);
            await session.Stream.FlushAsync(ct);
        });

        using var scanner = CreateScanner(server.Port);
        await using var content = new MemoryStream("check"u8.ToArray());
        var verdict = await scanner.ScanAsync(content, "check.txt", CancellationToken.None);
        Assert.Equal(TaskFileScanVerdict.NotConfigured, verdict);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mid_stream_connection_interruption_or_unterminated_reply_fails_closed(bool dropDuringChunks)
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db);
        using var storage = new TempFileStore();
        using var server = new FakeClamdServer(async (session, ct) =>
        {
            if (dropDuringChunks)
            {
                var buffer = new byte[14];
                _ = await session.Stream.ReadAsync(buffer, ct);
                session.Client.Close();
                return;
            }

            _ = await session.ReadInstreamPayloadAsync(ct);
            // Send "stream: OK" WITHOUT null or newline terminator and immediately close the connection.
            await session.WriteAsciiAsync("stream: OK", ct);
            session.Client.Close();
        });

        using var scanner = CreateScanner(server.Port, chunkSizeBytes: 1024);
        var controller = CreateController(db, 1, storage.Store, scanner, server.Port);
        var payload = new string('B', 4096);
        var result = await controller.UploadTask(1, MakeTextFile("interrupted.txt", payload), CancellationToken.None);

        Assert.Equal(503, StatusCode(result));
        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(storage.DiskFiles());
    }

    [Fact]
    public async Task Request_cancellation_during_scanning_aborts_promptly_and_releases_concurrency_slot()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db);
        using var storage = new TempFileStore();
        var scanStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdFirstScan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = 0;

        using var server = new FakeClamdServer(async (session, ct) =>
        {
            _ = await session.ReadInstreamPayloadAsync(ct);
            if (Interlocked.Increment(ref sessions) == 1)
            {
                scanStarted.TrySetResult();
                await holdFirstScan.Task.WaitAsync(ct);
            }
            await session.WriteAsciiAsync("stream: OK\0", ct);
        });

        using var scanner = CreateScanner(server.Port, maxConcurrentScans: 1);
        var controller = CreateController(db, 1, storage.Store, scanner, server.Port);

        using var cts = new CancellationTokenSource();
        var cancelledUpload = controller.UploadTask(1, MakeTextFile("cancel.txt", "cancel me"), cts.Token);
        await scanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledUpload);
        holdFirstScan.TrySetResult();
        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(storage.DiskFiles());

        // Verify the concurrency semaphore slot was released and the next upload succeeds.
        var subsequent = await controller.UploadTask(1, MakeTextFile("next.txt", "clean after cancel"), CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(subsequent);
        Assert.Single(await db.TaskAttachments.ToListAsync());
        Assert.Single(storage.DiskFiles());
    }

    [Fact]
    public async Task Concurrent_uploads_are_bounded_by_MaxConcurrentScans()
    {
        var active = 0;
        var maxObserved = 0;
        using var server = new FakeClamdServer(async (session, ct) =>
        {
            var current = Interlocked.Increment(ref active);
            UpdateMax(ref maxObserved, current);
            try
            {
                _ = await session.ReadInstreamPayloadAsync(ct);
                await Task.Delay(60, ct);
                await session.WriteAsciiAsync("stream: OK\0", ct);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        using var scanner = CreateScanner(server.Port, maxConcurrentScans: 2);
        var tasks = Enumerable.Range(0, 6).Select(async i =>
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"concurrent payload {i}"));
            return await scanner.ScanAsync(stream, $"file-{i}.txt", CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);
        Assert.All(results, verdict => Assert.Equal(TaskFileScanVerdict.Clean, verdict));
        Assert.InRange(maxObserved, 1, 2);
    }

    [Fact]
    public async Task Oversized_and_unsupported_files_are_rejected_before_reaching_ClamAV()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db);
        using var storage = new TempFileStore();
        var connections = 0;
        using var server = new FakeClamdServer(async (session, ct) =>
        {
            Interlocked.Increment(ref connections);
            _ = await session.ReadInstreamPayloadAsync(ct);
            await session.WriteAsciiAsync("stream: OK\0", ct);
        });

        var options = CreateOptions(server.Port, maxFileSizeBytes: 10);
        using var scanner = new ClamAvTaskFileScanner(Options.Create(options));
        var controller = CreateController(db, 1, storage.Store, scanner, server.Port, options);

        // Oversized (> 10 bytes)
        Assert.Equal(413, StatusCode(await controller.UploadTask(1, MakeTextFile("big.txt", "0123456789012345"), default)));

        // Unsupported extensions
        Assert.IsType<BadRequestObjectResult>(await controller.UploadTask(1, MakeTextFile("script.ps1", "echo 1"), default));
        Assert.IsType<BadRequestObjectResult>(await controller.UploadTask(1, MakeTextFile("binary.exe", "MZ"), default));

        // Spoofed signature (.pdf with plain text bytes)
        Assert.IsType<BadRequestObjectResult>(await controller.UploadTask(1, MakeTextFile("fake.pdf", "not-a-pdf"), default));

        Assert.Equal(0, connections);
        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(storage.DiskFiles());
    }

    [Fact]
    public async Task Permission_revocation_during_active_ClamAV_scan_denies_upload_and_cleans_up_files()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db);
        using var storage = new TempFileStore();
        using var server = new FakeClamdServer(async (session, ct) =>
        {
            _ = await session.ReadInstreamPayloadAsync(ct);
            await using var revokeContext = database.CreateContext();
            var revoked = await TaskCollaborationTestServices.TaskShares(revokeContext, 1).RemoveParticipantAsync(1, 2, 0);
            Assert.True(revoked.Success);
            await session.WriteAsciiAsync("stream: OK\0", ct);
        });

        using var scanner = CreateScanner(server.Port);
        var collaboratorController = CreateController(db, 2, storage.Store, scanner, server.Port);
        var result = await collaboratorController.UploadTask(1, MakeTextFile("revoked.txt", "secret"), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(storage.DiskFiles());
    }

    [Fact]
    public async Task Multi_file_discussion_batch_cleans_up_staged_first_file_when_second_file_is_infected_and_allows_idempotent_retry()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        await SeedPersonalAsync(db);
        using var storage = new TempFileStore();
        using var server = new FakeClamdServer(async (session, ct) =>
        {
            var payload = Encoding.UTF8.GetString(await session.ReadInstreamPayloadAsync(ct));
            var reply = payload.Contains("BAD_SECOND_FILE", StringComparison.Ordinal)
                ? "stream: Win.Malware.Test FOUND\0"
                : "stream: OK\0";
            await session.WriteAsciiAsync(reply, ct);
        });

        using var scanner = CreateScanner(server.Port);
        var controller = CreateController(db, 1, storage.Store, scanner, server.Port);
        var requestId = Guid.NewGuid();

        var failedBatch = new TaskAttachmentController.DiscussionDeliveryForm
        {
            ClientRequestId = requestId,
            Content = "Batch with second infected file",
            Files = [MakeTextFile("first-clean.txt", "Clean first file"), MakeTextFile("second-bad.txt", "BAD_SECOND_FILE")]
        };
        Assert.IsType<BadRequestObjectResult>(await controller.DeliverDiscussionMessage(1, failedBatch, default));
        Assert.Empty(await db.TaskMessages.ToListAsync());
        Assert.Empty(await db.TaskAttachments.ToListAsync());
        Assert.Empty(storage.DiskFiles());

        // Retrying the same ClientRequestId with two clean files succeeds and replays idempotently.
        var cleanBatch = new TaskAttachmentController.DiscussionDeliveryForm
        {
            ClientRequestId = requestId,
            Content = "Batch with clean files",
            Files = [MakeTextFile("first-clean.txt", "Clean first file"), MakeTextFile("second-clean.txt", "Clean second file")]
        };
        var firstAttempt = Assert.IsType<TaskMessageDto>(
            Assert.IsType<OkObjectResult>(await controller.DeliverDiscussionMessage(1, cleanBatch, default)).Value);
        var replayAttempt = Assert.IsType<TaskMessageDto>(
            Assert.IsType<OkObjectResult>(await controller.DeliverDiscussionMessage(
                1,
                new TaskAttachmentController.DiscussionDeliveryForm
                {
                    ClientRequestId = requestId,
                    Content = "Batch with clean files",
                    Files = [MakeTextFile("first-clean.txt", "Clean first file"), MakeTextFile("second-clean.txt", "Clean second file")]
                },
                default)).Value);

        Assert.Equal(firstAttempt.Id, replayAttempt.Id);
        Assert.Single(await db.TaskMessages.ToListAsync());
        Assert.Equal(2, await db.TaskAttachments.CountAsync());
        Assert.Equal(2, storage.DiskFiles().Length);
    }

    [Fact]
    public void Service_registration_enforces_fail_closed_defaults_and_development_localhost_restriction()
    {
        var defaultConfig = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var prodServices = new ServiceCollection();
        prodServices.AddSingleton<IWebHostEnvironment>(new FakeEnvironment(Environments.Production));
        prodServices.AddTaskFileServices(defaultConfig, new FakeEnvironment(Environments.Production));
        using var prodProvider = prodServices.BuildServiceProvider();
        Assert.IsType<NoOpTaskFileScanner>(prodProvider.GetRequiredService<ITaskFileScanner>());

        // Development rejects non-loopback scanner hosts
        var remoteDevConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TaskFiles:Scanner:Enabled"] = "true",
            ["TaskFiles:Scanner:Host"] = "10.20.30.40",
            ["TaskFiles:Scanner:Port"] = "3310"
        }).Build();
        var devServices = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            devServices.AddTaskFileServices(remoteDevConfig, new FakeEnvironment(Environments.Development)));

        // Development accepts 127.0.0.1 and registers ClamAvTaskFileScanner
        var localDevConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TaskFiles:Scanner:Enabled"] = "true",
            ["TaskFiles:Scanner:Host"] = "127.0.0.1",
            ["TaskFiles:Scanner:Port"] = "3310"
        }).Build();
        var localDevServices = new ServiceCollection();
        localDevServices.AddSingleton<IWebHostEnvironment>(new FakeEnvironment(Environments.Development));
        localDevServices.AddTaskFileServices(localDevConfig, new FakeEnvironment(Environments.Development));
        using var devProvider = localDevServices.BuildServiceProvider();
        Assert.IsType<ClamAvTaskFileScanner>(devProvider.GetRequiredService<ITaskFileScanner>());

        // Production accepts private service hostname when explicitly enabled
        var enabledProdConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TaskFiles:Scanner:Enabled"] = "true",
            ["TaskFiles:Scanner:Host"] = "clamav",
            ["TaskFiles:Scanner:Port"] = "3310"
        }).Build();
        var enabledProdServices = new ServiceCollection();
        enabledProdServices.AddSingleton<IWebHostEnvironment>(new FakeEnvironment(Environments.Production));
        enabledProdServices.AddTaskFileServices(enabledProdConfig, new FakeEnvironment(Environments.Production));
        using var enabledProdProvider = enabledProdServices.BuildServiceProvider();
        Assert.IsType<ClamAvTaskFileScanner>(enabledProdProvider.GetRequiredService<ITaskFileScanner>());
    }

    [Fact]
    public async Task Live_ClamAV_daemon_scans_clean_and_EICAR_payloads_when_reachable()
    {
        var host = Environment.GetEnvironmentVariable("CLAMAV_TEST_HOST") ?? "127.0.0.1";
        var port = int.TryParse(Environment.GetEnvironmentVariable("CLAMAV_TEST_PORT"), out var parsed) ? parsed : 3310;
        var options = CreateOptions(port);
        options.Scanner.Host = host;
        options.Scanner.ConnectTimeoutMs = 500;
        options.Scanner.ScanTimeoutMs = 10_000;

        using var scanner = new ClamAvTaskFileScanner(Options.Create(options));
        if (!await scanner.PingAsync())
        {
            // Local ClamAV container is not running in this test process environment; protocol tests above cover FakeClamdServer.
            return;
        }

        await using var cleanStream = new MemoryStream("Harmless verification document"u8.ToArray());
        Assert.Equal(TaskFileScanVerdict.Clean, await scanner.ScanAsync(cleanStream, "clean.txt", CancellationToken.None));

        // Assemble standard EICAR test string dynamically in memory so no literal antivirus trigger is stored on disk.
        var eicar = string.Concat(
            "X5O!P%@AP[4\\PZX54(P^)7CC)7}",
            "$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        await using var eicarStream = new MemoryStream(Encoding.ASCII.GetBytes(eicar));
        Assert.Equal(TaskFileScanVerdict.Rejected, await scanner.ScanAsync(eicarStream, "eicar.txt", CancellationToken.None));
    }

    private static async Task<SubTaskDto> SeedAllUploadContextsAsync(TaskTrackerDbContext db)
    {
        await SeedPersonalAsync(db);
        var task = await db.TaskRequests.SingleAsync(x => x.Id == 1);
        task.Status = TaskStatus.InProgress;
        db.TaskMessages.Add(new TaskMessage { Id = 1, TaskRequestId = 1, SenderUserId = 1, Content = "Legacy discussion message" });

        var secondTask = TestDatabase.Task(2);
        secondTask.Status = TaskStatus.InReview;
        secondTask.AssigneeUserId = 2;
        db.TaskRequests.Add(secondTask);
        db.TaskShares.Add(new TaskShare { TaskRequestId = 2, SharedWithUserId = 2, Permission = TaskPermission.Edit });
        db.TaskSubmissions.Add(new TaskSubmission
        {
            Id = 1, TaskRequestId = 2, SubmittedByUserId = 2, RevisionNumber = 1, Content = "Parent submission"
        });
        await db.SaveChangesAsync();

        var subTaskService = TaskCollaborationTestServices.SubTasks(db);
        var subTask = await StartDelegatedAsync(subTaskService, 2, "Subtask upload check");
        return Data(await subTaskService.SubmitAsync(1, subTask.Id, new() { Version = subTask.Version, Content = "Subtask work" }, 2));
    }

    private static ClamAvTaskFileScanner CreateScanner(
        int port,
        int connectTimeoutMs = 2000,
        int scanTimeoutMs = 5000,
        int maxConcurrentScans = 4,
        int chunkSizeBytes = 64 * 1024) =>
        new(Options.Create(CreateOptions(port, connectTimeoutMs, scanTimeoutMs, maxConcurrentScans, chunkSizeBytes)));

    private static TaskFileOptions CreateOptions(
        int port,
        int connectTimeoutMs = 2000,
        int scanTimeoutMs = 5000,
        int maxConcurrentScans = 4,
        int chunkSizeBytes = 64 * 1024,
        long maxFileSizeBytes = 20 * 1024 * 1024,
        bool requireCleanScan = true) =>
        new()
        {
            MaxFileSizeBytes = maxFileSizeBytes,
            RequireCleanScan = requireCleanScan,
            EnableDiscussionUploads = true,
            EnableSubTaskSubmissionUploads = true,
            Scanner = new ClamAvScannerOptions
            {
                Enabled = true,
                Host = "127.0.0.1",
                Port = port,
                ConnectTimeoutMs = connectTimeoutMs,
                ScanTimeoutMs = scanTimeoutMs,
                MaxConcurrentScans = maxConcurrentScans,
                ChunkSizeBytes = chunkSizeBytes
            }
        };

    private static TaskAttachmentController CreateController(
        TaskTrackerDbContext db,
        int userId,
        ITaskFileStore store,
        ITaskFileScanner scanner,
        int port,
        TaskFileOptions? options = null)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "User")], "test"))
        };
        var collaboration = new TaskCollaborationManager(
            new UnitOfWork(db),
            new EfTaskCollaborationDal(db),
            new NoOpRealtime(),
            NullLogger<TaskCollaborationManager>.Instance,
            new EfSubTaskDal(db));

        return new TaskAttachmentController(
            db,
            new Actor(userId),
            collaboration,
            store,
            scanner,
            Options.Create(options ?? CreateOptions(port)),
            new EfSubTaskDal(db))
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static IFormFile MakeTextFile(string name, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name);
    }

    private static int? StatusCode(IActionResult result) => Assert.IsType<ObjectResult>(result).StatusCode;

    private static int GetUnusedLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void UpdateMax(ref int target, int candidate)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref target);
            if (candidate <= snapshot) return;
            if (Interlocked.CompareExchange(ref target, candidate, snapshot) == snapshot) return;
        }
    }

    private sealed class Actor(int userId) : ICurrentUserService
    {
        public int UserId => userId;
    }

    private sealed class NoOpRealtime : ITaskCollaborationRealtimeService
    {
        public Task MessageCreatedAsync(int taskId, TaskMessageDto message) => Task.CompletedTask;
        public Task AttachmentsChangedAsync(int taskId) => Task.CompletedTask;
        public Task ActivityCreatedAsync(int taskId, TaskActivityDto activity) => Task.CompletedTask;
        public Task TaskChangedAsync(int taskId) => Task.CompletedTask;
        public Task WorkspaceRosterChangedAsync(int workspaceId, IReadOnlyList<int> taskIds) => Task.CompletedTask;
        public Task AccessRevokedAsync(int taskId, int userId) => Task.CompletedTask;
    }

    private sealed class FakeEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "TaskTracker.API";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TempFileStore : IDisposable
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "tasktracker-clamav-tests-" + Guid.NewGuid().ToString("N"));
        public PrivateTaskFileStore Store { get; }

        public TempFileStore()
        {
            Directory.CreateDirectory(RootPath);
            Store = new PrivateTaskFileStore(
                Options.Create(new TaskFileOptions { RootPath = RootPath }),
                new FakeEnvironment(Environments.Development) { ContentRootPath = RootPath });
        }

        public string[] DiskFiles() =>
            Directory.Exists(RootPath) ? Directory.GetFiles(RootPath) : [];

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // Ignore best-effort temp directory cleanup errors.
            }
        }
    }

    private sealed class FakeClamdServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;
        private readonly ConcurrentBag<Task> _sessions = [];

        public int Port { get; }

        public FakeClamdServer(Func<FakeClamdSession, CancellationToken, Task> handler)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (SocketException)
                    {
                        break;
                    }

                    _sessions.Add(Task.Run(async () =>
                    {
                        using (client)
                        await using (var stream = client.GetStream())
                        {
                            var session = new FakeClamdSession(client, stream);
                            try
                            {
                                var command = await session.ReadCommandAsync(_cts.Token);
                                if (command == "zPING")
                                {
                                    await session.WriteAsciiAsync("PONG\0", _cts.Token);
                                    return;
                                }
                                Assert.Equal("zINSTREAM", command);
                                await handler(session, _cts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                            }
                            catch (IOException)
                            {
                            }
                            catch (SocketException)
                            {
                            }
                        }
                    }));
                }
            });
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            try { _acceptLoop.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }
    }

    private sealed class FakeClamdSession(TcpClient client, NetworkStream stream)
    {
        public TcpClient Client { get; } = client;
        public NetworkStream Stream { get; } = stream;
        public int ChunkCount { get; private set; }

        public async Task<string> ReadCommandAsync(CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            var single = new byte[1];
            while (true)
            {
                var read = await Stream.ReadAsync(single.AsMemory(0, 1), ct);
                if (read <= 0 || single[0] == 0 || single[0] == (byte)'\n')
                    break;
                buffer.WriteByte(single[0]);
            }
            return Encoding.ASCII.GetString(buffer.ToArray());
        }

        public async Task<byte[]> ReadInstreamPayloadAsync(CancellationToken ct)
        {
            using var payload = new MemoryStream();
            var lengthHeader = new byte[4];
            while (true)
            {
                await Stream.ReadExactlyAsync(lengthHeader, ct);
                var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(lengthHeader);
                if (chunkLength == 0)
                    break;

                ChunkCount++;
                var chunk = new byte[chunkLength];
                await Stream.ReadExactlyAsync(chunk, ct);
                payload.Write(chunk, 0, chunk.Length);
            }
            return payload.ToArray();
        }

        public async Task WriteAsciiAsync(string text, CancellationToken ct)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            await Stream.WriteAsync(bytes, ct);
            await Stream.FlushAsync(ct);
        }
    }
}
