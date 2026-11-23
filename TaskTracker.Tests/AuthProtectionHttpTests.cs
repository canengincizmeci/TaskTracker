using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskTracker.API.Controllers;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Bussiness.ValidationRules.FluentValidation;
using TaskTracker.Core.Extensions;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class AuthProtectionHttpTests
{
    [Theory]
    [InlineData("login", 20, 10)]
    [InlineData("register", 8, 3)]
    [InlineData("refresh-token", 60, 20)]
    [InlineData("forgot-password", 8, 3)]
    [InlineData("verify-password-reset-code", 24, 10)]
    [InlineData("reset-password", 12, 6)]
    public async Task Each_auth_route_enforces_ip_and_identity_limits(string route, int ipLimit, int identityLimit)
    {
        await using var server = await AuthTestHost.StartAsync("127.0.0.1");
        for (var i = 0; i < ipLimit; i++)
            await AssertAdmittedAsync(server, route, i, "192.0.2.10");
        using var ipRejection = await server.PostAsync(route, Body(route, ipLimit), "192.0.2.10");
        AssertRetryAfter(ipRejection);

        const int identity = 100;
        for (var i = 0; i < identityLimit; i++)
            await AssertAdmittedAsync(server, route, identity, $"198.51.100.{i + 1}", variedCase: i % 2 == 0);
        using var identityRejection = await server.PostAsync(route, Body(route, identity), "198.51.100.200");
        AssertRetryAfter(identityRejection);
        await AssertAdmittedAsync(server, route, identity + 1, "198.51.100.200");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("192.0.2.72")]
    public async Task Unconfigured_or_untrusted_peers_cannot_change_the_ip_bucket(string? knownProxy)
    {
        await using var server = await AuthTestHost.StartAsync(knownProxy);
        for (var i = 0; i < 8; i++)
            await AssertAdmittedAsync(server, "forgot-password", i, $"198.51.100.{i + 1}");
        using var rejected = await server.PostAsync("forgot-password", Body("forgot-password", 9), "192.0.2.7");
        AssertRetryAfter(rejected);
    }

    [Fact]
    public async Task Trusted_proxy_contributes_only_its_nearest_forwarded_address()
    {
        await using var server = await AuthTestHost.StartAsync("127.0.0.1");
        for (var i = 0; i < 8; i++)
            await AssertAdmittedAsync(server, "forgot-password", i, $"198.51.100.{i + 1}, 192.0.2.22");
        using var rejected = await server.PostAsync("forgot-password", Body("forgot-password", 9), "198.51.100.55, 192.0.2.22");
        AssertRetryAfter(rejected);
        await AssertAdmittedAsync(server, "forgot-password", 10, "192.0.2.23");
    }

    [Fact]
    public async Task Registration_limits_the_same_username_across_emails_and_ips()
    {
        await using var server = await AuthTestHost.StartAsync("127.0.0.1");
        for (var i = 0; i < 4; i++)
        {
            using var response = await server.PostAsync("register", new
            {
                email = $"user{i}@example.test", userName = i % 2 == 0 ? "  SameUser  " : "sameuser",
                firstName = "Rate", lastName = "Test", password = "Synthetic account password"
            }, $"192.0.2.{i + 1}");
            if (i < 3) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            else AssertRetryAfter(response);
        }
    }

    [Fact]
    public async Task Global_throughput_budget_is_shared_across_routes_and_ips()
    {
        await using var server = await AuthTestHost.StartAsync("127.0.0.1");
        for (var i = 0; i < 300; i++)
            await AssertAdmittedAsync(server, i % 2 == 0 ? "login" : "reset-password", i,
                $"198.51.{100 + i / 250}.{1 + i % 250}");
        using var rejected = await server.PostAsync("forgot-password", Body("forgot-password", 400), "192.0.2.40");
        AssertRetryAfter(rejected);
    }

    [Fact]
    public async Task Global_concurrency_budget_rejects_excess_requests_without_queueing()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        await using var server = await AuthTestHost.StartAsync("127.0.0.1", probe =>
            probe.LoginGate = async () =>
            {
                if (Interlocked.Increment(ref active) == 24) entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
            });
        var requests = Enumerable.Range(1, 24).Select(i =>
            server.PostAsync("login", Body("login", i), $"192.0.2.{i}")).ToArray();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var rejected = await server.PostAsync("register", Body("register", 500), "192.0.2.100");
            AssertRetryAfter(rejected);
        }
        finally
        {
            release.TrySetResult();
            foreach (var response in await Task.WhenAll(requests))
                response.Dispose();
        }
        await AssertAdmittedAsync(server, "register", 501, "192.0.2.101");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validation_response_and_logged_exception_exclude_passwords_tokens_and_placeholders(bool rawFailure)
    {
        const string newPassword = "synthetic-new-password-39f1";
        const string confirmation = "synthetic-confirmation-77c0";
        const string token = "synthetic-reset-token-1d4a";
        await using var server = await AuthTestHost.StartAsync(configure: probe => probe.ValidateReset = (dto, logger) =>
        {
            var validator = new ResetPasswordDtoValidator();
            validator.RuleFor(item => item.ResetToken).Must(_ => false)
                .WithMessage(item => $"Invalid token {item.ResetToken}: {{PropertyValue}}")
                .WithErrorCode(token).WithState(item => new { item.ResetToken, item.NewPassword });
            if (rawFailure)
            {
                logger.LogWarning("Starting raw validation");
                validator.ValidateAndThrow(dto);
            }
            else
            {
                var failure = Assert.Throws<ValidationException>(() =>
                    TaskTracker.Core.CrossCuttingConcerns.Validation.ValidationTool.Validate(validator, dto));
                logger.LogWarning(failure, "Sanitized validation exception");
                throw failure;
            }
        });
        using var response = await server.PostAsync("reset-password", new
        { resetToken = token, newPassword, confirmNewPassword = confirmation });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.Equal("validation_failed", document.RootElement.GetProperty("Code").GetString());
        var errors = document.RootElement.GetProperty("Errors").EnumerateArray().ToArray();
        Assert.Equal(2, errors.Length);
        Assert.All(errors, error =>
            Assert.Equal(new[] { "ErrorCode", "ErrorMessage", "PropertyName" },
                error.EnumerateObject().Select(property => property.Name).Order().ToArray()));
        Assert.Contains(errors, error => error.GetProperty("PropertyName").GetString() == "ConfirmNewPassword" &&
            error.GetProperty("ErrorCode").GetString() == "fields_do_not_match");
        Assert.Contains(errors, error => error.GetProperty("PropertyName").GetString() == "ResetToken" &&
            error.GetProperty("ErrorCode").GetString() == "invalid_value");

        var logs = string.Join("\n", server.Logs.Messages);
        Assert.Contains(rawFailure ? "Starting raw validation" : "Sanitized validation exception", logs);
        foreach (var secret in new[] { newPassword, confirmation, token, "AttemptedValue", "CustomState",
                     "FormattedMessagePlaceholderValues", "ComparisonValue", "PropertyValue" })
        {
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }
    }

    private static async Task AssertAdmittedAsync(AuthTestHost server, string route, int identity, string ip, bool variedCase = false)
    {
        using var response = await server.PostAsync(route, Body(route, identity, variedCase), ip);
        Assert.Equal(route is "login" or "refresh-token" or "verify-password-reset-code" or "reset-password"
            ? HttpStatusCode.BadRequest : HttpStatusCode.OK, response.StatusCode);
    }

    private static object Body(string route, int identity, bool variedCase = false)
    {
        var email = variedCase ? $"  USER{identity}@EXAMPLE.TEST  " : $"user{identity}@example.test";
        const string password = "Synthetic user password";
        return route switch
        {
            "login" => new { email, password },
            "register" => new { email, userName = $"rate-user{identity}", password, firstName = "Rate", lastName = "Test" },
            "refresh-token" => new { refreshToken = $"refresh-credential-{identity}" },
            "verify-password-reset-code" => new { email, code = "123456" },
            "reset-password" => new { resetToken = $"reset-credential-{identity}", newPassword = password, confirmNewPassword = password },
            _ => new { email }
        };
    }

    private static void AssertRetryAfter(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    private sealed class AuthTestHost(WebApplication app, HttpClient client, CapturedLogs logs) : IAsyncDisposable
    {
        internal CapturedLogs Logs { get; } = logs;

        internal static async Task<AuthTestHost> StartAsync(string? knownProxy = null, Action<AuthProbe>? configure = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.Configuration.AddInMemoryCollection(knownProxy is null ? [] : new Dictionary<string, string?>
                { ["ReverseProxy:KnownProxies:0"] = knownProxy });
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
            var logs = new CapturedLogs();
            builder.Logging.ClearProviders();
            builder.Logging.AddFilter<CapturedLogs>(null, LogLevel.Trace);
            builder.Logging.AddProvider(logs);
            builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
            builder.Services.AddSingleton<IIdentityNormalizer, IdentityNormalizer>();
            builder.Services.AddSingleton<IAuthService>(services =>
            {
                var probe = new AuthProbe(services.GetRequiredService<ILogger<AuthProbe>>());
                configure?.Invoke(probe);
                return probe;
            });
            builder.Services.AddTrustedProxyHeaders(builder.Configuration);
            builder.Services.AddAuthRateLimits();
            var app = builder.Build();
            app.UseForwardedHeaders();
            app.UseMiddleware<ExceptionMiddleware>();
            app.UseRouting();
            app.UseRateLimiter();
            app.MapControllers();
            await app.StartAsync();
            return new(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }, logs);
        }

        internal async Task<HttpResponseMessage> PostAsync(string route, object body, string? forwardedFor = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/" + route);
            request.Content = JsonContent.Create(body);
            if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
            return await client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class AuthProbe(ILogger<AuthProbe> logger) : IAuthService
    {
        internal Func<Task>? LoginGate { get; set; }
        internal Action<ResetPasswordDto, ILogger>? ValidateReset { get; set; }

        public async Task<IDataResult<LoginResponseDto>> LoginAsync(UserForLoginDto dto)
        {
            if (LoginGate is not null) await LoginGate();
            return new ErrorDataResult<LoginResponseDto>(Messages.PasswordError);
        }

        public Task<IResult> RegisterAsync(UserForRegisterDto dto) => Task.FromResult<IResult>(new SuccessResult());
        public Task<IDataResult<TokenResponseDto>> RefreshTokenAsync(RefreshTokenDto dto) =>
            Task.FromResult<IDataResult<TokenResponseDto>>(new ErrorDataResult<TokenResponseDto>(Messages.RefreshTokenInvalid));
        public Task<IResult> ForgotPasswordAsync(ForgotPasswordDto dto) =>
            Task.FromResult<IResult>(new SuccessResult(Messages.PasswordRecoveryInstructionsSent));
        public Task<IDataResult<PasswordResetTokenDto>> VerifyPasswordResetCodeAsync(VerifyPasswordResetCodeDto dto) =>
            Task.FromResult<IDataResult<PasswordResetTokenDto>>(new ErrorDataResult<PasswordResetTokenDto>(
                Messages.PasswordResetCodeInvalidOrExpired));
        public Task<IResult> ResetPasswordAsync(ResetPasswordDto dto)
        {
            ValidateReset?.Invoke(dto, logger);
            return Task.FromResult<IResult>(new ErrorResult(Messages.PasswordResetTokenInvalidOrExpired));
        }
        public Task LogoutAsync(RefreshTokenDto dto) => Task.CompletedTask;
        public Task<IResult> VerifyEmailAsync(EmailVerificationDto dto) => Task.FromResult<IResult>(new ErrorResult());
        public Task<IResult> ResendVerificationAsync(ResendVerificationDto dto) => Task.FromResult<IResult>(new SuccessResult());
        public Task<IResult> ChangePasswordAsync(ChangePasswordDto dto) => Task.FromResult<IResult>(new ErrorResult());
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        internal ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }

        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception) + exception);
        }
    }
}
