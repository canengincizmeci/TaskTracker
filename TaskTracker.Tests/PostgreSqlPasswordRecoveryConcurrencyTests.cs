using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Core.Utilities.Security.Cryptography;
using TaskTracker.Core.Utilities.Security.Jwt;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class PostgreSqlPasswordRecoveryConcurrencyTests
{
    private const string Email = "pg-user1@example.test";
    private const string CorrectCode = "123456";
    private static readonly string HmacSecret = new('t', 64);

    [PostgreSqlFact]
    public async Task Parallel_wrong_codes_record_exactly_five_attempts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedAsync(database);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = database.CreateContext();
            Assert.False((await Manager(context).VerifyPasswordResetCodeAsync(Code("654321"))).Success);
        }));

        await using var verify = database.CreateContext();
        var request = await verify.PasswordResetRequests.SingleAsync();
        Assert.Equal(5, request.FailedAttemptCount);
        Assert.Equal(request.ExpiresAt, request.LockedUntil);
        Assert.Null(request.VerifiedAt);
        Assert.False((await Manager(verify).VerifyPasswordResetCodeAsync(Code(CorrectCode))).Success);
    }

    [PostgreSqlFact]
    public async Task Correct_code_waiting_behind_the_fifth_wrong_attempt_cannot_unlock_it()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedAsync(database, failedAttempts: 4);
        var beforeWrite = new PostgreSqlCommandGate("UPDATE \"PasswordResetRequests\"", pause: true);
        var beforeLock = new PostgreSqlCommandGate("FOR UPDATE", pause: false);
        await using var first = database.CreateContext(beforeWrite);
        await using var second = database.CreateContext(beforeLock);
        var wrong = Manager(first).VerifyPasswordResetCodeAsync(Code("654321"));
        Task<IDataResult<PasswordResetTokenDto>> correct;
        try
        {
            await beforeWrite.Arrived;
            correct = Manager(second).VerifyPasswordResetCodeAsync(Code(CorrectCode));
            await beforeLock.Arrived;
        }
        finally
        {
            beforeWrite.Release();
        }
        Assert.False((await wrong).Success);
        Assert.False((await correct).Success);
        await using var verify = database.CreateContext();
        var request = await verify.PasswordResetRequests.SingleAsync();
        Assert.Equal(5, request.FailedAttemptCount);
        Assert.Null(request.ResetTokenHash);
    }

    [PostgreSqlFact]
    public async Task Parallel_correct_codes_issue_one_reset_token()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedAsync(database);
        var beforeWrite = new PostgreSqlCommandGate("UPDATE \"PasswordResetRequests\"", pause: true);
        var beforeLock = new PostgreSqlCommandGate("FOR UPDATE", pause: false);
        await using var first = database.CreateContext(beforeWrite);
        await using var second = database.CreateContext(beforeLock);
        var winner = Manager(first).VerifyPasswordResetCodeAsync(Code(CorrectCode));
        Task<IDataResult<PasswordResetTokenDto>> competitor;
        try
        {
            await beforeWrite.Arrived;
            competitor = Manager(second).VerifyPasswordResetCodeAsync(Code(CorrectCode));
            await beforeLock.Arrived;
        }
        finally
        {
            beforeWrite.Release();
        }

        var result = await winner;
        Assert.True(result.Success);
        Assert.False((await competitor).Success);
        await using var verify = database.CreateContext();
        var request = await verify.PasswordResetRequests.SingleAsync();
        Assert.NotNull(request.VerifiedAt);
        Assert.Equal(PasswordResetTokenGenerator.HashToken(result.Data.ResetToken), request.ResetTokenHash);
    }

    [PostgreSqlFact]
    public async Task Concurrent_forgot_requests_observe_the_same_resend_cooldown()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedAsync(database, createRequest: false);
        var deliveries = 0;
        var email = new RecoveryEmail((_, _) =>
        {
            Interlocked.Increment(ref deliveries);
            return Task.CompletedTask;
        });
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();

        var results = await Task.WhenAll(
            Manager(first, email).ForgotPasswordAsync(new() { Email = Email }),
            Manager(second, email).ForgotPasswordAsync(new() { Email = Email }));
        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(results[0].Message, results[1].Message);
        Assert.Equal(1, deliveries);
        await using var verify = database.CreateContext();
        Assert.Single(await verify.PasswordResetRequests.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task A_late_delivery_failure_does_not_overwrite_a_completed_verification()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedAsync(database, createRequest: false);
        var codeSent = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var email = new RecoveryEmail(async (_, code) =>
        {
            codeSent.SetResult(code);
            await failDelivery.Task.WaitAsync(TimeSpan.FromSeconds(15));
            throw new IOException("Simulated delivery failure after sending.");
        });
        await using var issuing = database.CreateContext();
        await using var verifying = database.CreateContext();
        var forgot = Manager(issuing, email).ForgotPasswordAsync(new() { Email = Email });
        try
        {
            var code = await codeSent.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True((await Manager(verifying).VerifyPasswordResetCodeAsync(Code(code))).Success);
        }
        finally
        {
            failDelivery.TrySetResult();
        }
        Assert.True((await forgot).Success);
        await using var check = database.CreateContext();
        var request = await check.PasswordResetRequests.SingleAsync();
        Assert.NotNull(request.VerifiedAt);
        Assert.NotNull(request.ResetTokenHash);
        Assert.Null(request.InvalidatedAt);
    }

    private static VerifyPasswordResetCodeDto Code(string code) => new() { Email = Email, Code = code };

    private static async Task SeedAsync(PostgreSqlTestDatabase database, int failedAttempts = 0, bool createRequest = true)
    {
        await using var context = database.CreateContext();
        var user = await context.Users.SingleAsync(user => user.Id == 1);
        user.IsVerified = true;
        if (createRequest)
            context.PasswordResetRequests.Add(new PasswordResetRequest
            {
                UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddMinutes(10), FailedAttemptCount = failedAttempts,
                CodeHash = PasswordResetCodeHasher.Hash(Email, CorrectCode, HmacSecret)
            });
        await context.SaveChangesAsync();
    }

    private static AuthManager Manager(TaskTrackerDbContext context, IEmailService? email = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PasswordRecovery:HmacSecret"] = HmacSecret,
            ["TokenOptions:AccessTokenExpiration"] = "30"
        }).Build();
        return new(new UnitOfWork(context), new JwtHelper(config), email ?? new RecoveryEmail((_, _) => Task.CompletedTask),
            new CurrentUser(), config, NullLogger<AuthManager>.Instance, PasswordHashServiceTests.Service(), new IdentityNormalizer());
    }

    private sealed class CurrentUser : ICurrentUserService
    {
        public int UserId => 1;
    }

    private sealed class RecoveryEmail(Func<string, string, Task> send) : IEmailService
    {
        public Task SendPasswordResetCodeAsync(string email, string code) => send(email, code);
        public Task SendVerificationCodeAsync(string email, string code) => Task.CompletedTask;
        public Task SendTaskShareInvitationEmailAsync(string email, string taskTitle, string inviterUsername,
            string invitationUrl) => Task.CompletedTask;
    }
}
