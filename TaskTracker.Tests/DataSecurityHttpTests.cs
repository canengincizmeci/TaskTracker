using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TaskTracker.API.Controllers;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Core.Utilities.Security.Jwt;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class DataSecurityHttpTests
{
    private const string Issuer = "batch04-test-issuer";
    private const string Audience = "batch04-test-audience";
    private static readonly string SigningKey = new('s', 64);

    [Theory]
    [InlineData("/hubs/notifications")]
    [InlineData("/hubs/task-workspace")]
    public async Task Hub_query_credential_connects_and_is_absent_from_errors_logs_and_trace_tags(string path)
    {
        var logs = new CapturedLogs();
        var builder = CreateBuilder();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSignalR();
        await using var app = builder.Build();
        var requests = new ConcurrentQueue<string>();
        app.Use(async (context, next) =>
        {
            using var activity = new Activity("test-http-instrumentation").Start();
            activity.SetTag("http.target", context.Features.Get<IHttpRequestFeature>()!.RawTarget);
            activity.SetTag("url.query", context.Request.QueryString.Value);
            await next(context);
            requests.Enqueue(string.Join(' ', activity.TagObjects.Select(item => item.Value)));
        });
        app.Use(RequestCredentialProtection.RemoveQueryTokenAsync);
        app.Use(async (context, next) =>
        {
            requests.Enqueue(context.Features.Get<IHttpRequestFeature>()!.RawTarget);
            await next(context);
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<ProbeHub>(path).RequireAuthorization();
        app.MapGet("/outside-hub", () => "unexpected").RequireAuthorization();
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var credential = IssueToken();
        using var negotiate = new HttpRequestMessage(HttpMethod.Post, $"{path}/negotiate?negotiateVersion=1");
        negotiate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        using var negotiated = await client.SendAsync(negotiate);
        Assert.Equal(HttpStatusCode.OK, negotiated.StatusCode);

        const string invalidToken = "synthetic-invalid-access-token-54cb";
        using var denied = await client.GetAsync($"{path}?%61ccess_token={invalidToken}&keep=1");
        using var outside = await client.GetAsync($"/outside-hub?access_token={credential}");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, outside.StatusCode);
        Assert.False(denied.ToString().Contains(invalidToken, StringComparison.Ordinal));
        Assert.DoesNotContain(invalidToken, await denied.Content.ReadAsStringAsync());

        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var socketUrl = new UriBuilder(client.BaseAddress!) { Scheme = "ws", Path = path, Query = $"access_token={credential}&keep=1" };
        await socket.ConnectAsync(socketUrl.Uri, timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
            WebSocketMessageType.Text, true, timeout.Token);
        var handshake = new byte[128];
        var response = await socket.ReceiveAsync(new ArraySegment<byte>(handshake), timeout.Token);
        Assert.StartsWith("{}\u001e", Encoding.UTF8.GetString(handshake, 0, response.Count));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token);
        await app.StopAsync();

        Assert.Contains($"{path}?keep=1", requests);
        foreach (var secret in new[] { credential, invalidToken, "private-user@example.test", "Synthetic Identity" })
        {
            Assert.DoesNotContain(logs.Entries, line => line.Contains(secret, StringComparison.Ordinal));
            Assert.DoesNotContain(requests, line => line.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Ml_http_export_and_eligibility_share_capacity_and_disable_caching()
    {
        var builder = CreateBuilder();
        var service = new DatasetProbe();
        builder.Services.AddSingleton<ITaskMlDatasetService>(service);
        builder.Services.AddSingleton<ICurrentUserService, CurrentUser>();
        builder.Services.AddSingleton<MlDatasetResourceFilter>();
        builder.Services.AddControllers().AddApplicationPart(typeof(MlDatasetController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken());
        var export = client.GetAsync("/api/ml-dataset/export");
        var eligibility = client.GetAsync("/api/ml-dataset/eligibility");
        try
        {
            await service.Full.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var busy = await client.GetAsync("/api/ml-dataset/export");
            Assert.Equal(HttpStatusCode.TooManyRequests, busy.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), busy.Headers.RetryAfter?.Delta);
            Assert.True(busy.Headers.CacheControl?.NoStore);
        }
        finally
        {
            service.Release.TrySetResult();
            using var csv = await export;
            using var analysis = await eligibility;
            Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
            Assert.Equal(HttpStatusCode.OK, analysis.StatusCode);
            Assert.True(csv.Headers.CacheControl?.NoStore);
            Assert.True(analysis.Headers.CacheControl?.NoStore);
        }

        using var overBudget = await client.GetAsync("/api/ml-dataset/export?workspaceId=42");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, overBudget.StatusCode);
        Assert.True(overBudget.Headers.CacheControl?.NoStore);
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        RequestCredentialProtection.ConfigureLogging(builder.Logging);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.IncludeErrorDetails = false;
            options.Events = new JwtBearerEvents { OnMessageReceived = RequestCredentialProtection.ReadHubTokenAsync };
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey))
            };
        });
        builder.Services.AddAuthorization();
        return builder;
    }

    private static string IssueToken()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TokenOptions:Issuer"] = Issuer,
            ["TokenOptions:Audience"] = Audience,
            ["TokenOptions:SecurityKey"] = SigningKey,
            ["TokenOptions:AccessTokenExpiration"] = "5"
        }).Build();
        return new JwtHelper(configuration).CreateToken(new User
        {
            Id = 1, Email = "private-user@example.test", FirstName = "Synthetic", LastName = "Identity"
        }, [new OperationClaim { Name = "User" }], Guid.NewGuid()).Token;
    }

    public sealed class ProbeHub : Hub;
    private sealed class CurrentUser : ICurrentUserService { public int UserId => 1; }

    private sealed class DatasetProbe : ITaskMlDatasetService
    {
        private int active;
        public TaskCompletionSource Full { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskMlDatasetSchemaDto GetSchema() => throw new NotSupportedException();

        public async Task<IDataResult<TaskMlDatasetExportDto>> ExportCsvAsync(int currentUserId, int? workspaceId = null,
            bool eligibleOnly = false, CancellationToken ct = default)
        {
            if (workspaceId == 42)
                return new ErrorDataResult<TaskMlDatasetExportDto>(TaskMlDatasetManager.DatasetLimitExceeded);
            await RunAsync(ct);
            return new SuccessDataResult<TaskMlDatasetExportDto>(new TaskMlDatasetExportDto
            {
                CsvContent = "task_id\n1\n", FileName = "fixture.csv", SchemaVersion = "1.0", Columns = [], Rows = []
            });
        }

        public async Task<IDataResult<List<TaskHistoricalEligibilityDto>>> AnalyzeEligibilityAsync(int currentUserId,
            int? workspaceId = null, CancellationToken ct = default)
        {
            await RunAsync(ct);
            return new SuccessDataResult<List<TaskHistoricalEligibilityDto>>([]);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref active) == MlDatasetResourceFilter.MaxConcurrentRequests)
                Full.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries);
        public void Dispose() { }

        private sealed class CaptureLogger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => entries.Enqueue(formatter(state, exception) + exception);
        }
    }
}
