using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.API.Controllers;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Security.Cryptography;
using TaskTracker.Core.Utilities.Security.Hashing;
using TaskTracker.Core.Utilities.Security.Jwt;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class AuthPasswordHardeningTests
{
    private const string LegacyPassword = "legacy";
    private const string NewPassword = "new password!";
    private const string WinningPassword = "winning password!";
    private const string LosingPassword = "losing password!";

    [Fact]
    public async Task RegistrationWritesIdentityV3()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var manager = Manager(context);

        var result = await manager.RegisterAsync(new UserForRegisterDto
        {
            Email = "new@example.test",
            UserName = "new-user",
            FirstName = "New",
            LastName = "User",
            Password = NewPassword
        });

        var user = await context.Users.SingleAsync(x => x.Email == "new@example.test");
        Assert.True(result.Success);
        Assert.Equal(PasswordHashVersion.IdentityV3, user.PasswordHashVersion);
        Assert.Empty(user.PasswordSalt);
        Assert.Equal(PasswordVerificationOutcome.Valid, Hasher().Verify(user, NewPassword));
    }

    [Fact]
    public async Task RegistrationCanonicalizesEmailAndPreservesTrimmedUserNameDisplay()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();

        var result = await Manager(context).RegisterAsync(new UserForRegisterDto
        {
            Email = "  New.User@EXAMPLE.TEST  ",
            UserName = "  DisplayName  ",
            FirstName = "New",
            LastName = "User",
            Password = NewPassword
        });

        var user = await context.Users.SingleAsync(x => x.Email == "new.user@example.test");
        Assert.True(result.Success);
        Assert.Equal("DisplayName", user.UserName);
        Assert.Equal("displayname", user.NormalizedUserName);
    }

    [Theory]
    [InlineData("  USER1@EXAMPLE.TEST  ", "different-name")]
    [InlineData("different@example.test", "  UsEr1  ")]
    public async Task RegistrationRejectsCanonicalIdentityConflictsWithoutOverwritingTheAccount(
        string email,
        string userName)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var original = await context.Users.SingleAsync(x => x.Id == 1);

        var result = await Manager(context).RegisterAsync(new UserForRegisterDto
        {
            Email = email,
            UserName = userName,
            FirstName = "Replacement",
            LastName = "Attempt",
            Password = NewPassword
        });

        Assert.IsAssignableFrom<TaskTracker.Core.Utilities.Results.IConflictResult>(result);
        Assert.Equal(Messages.IdentityConflict, result.Message);
        Assert.Equal("Test", original.FirstName);
        Assert.Equal("user1", original.UserName);
        Assert.Equal(3, await context.Users.CountAsync());
    }

    [Fact]
    public async Task ExactRegistrationDuplicateMapsToStableIdentityConflictResponse()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var controller = new AuthController(Manager(context));

        var response = await controller.Register(new UserForRegisterDto
        {
            Email = "user1@example.test",
            UserName = "user1",
            FirstName = "Duplicate",
            LastName = "User",
            Password = NewPassword
        });

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("identity_conflict",
            conflict.Value!.GetType().GetProperty("code")!.GetValue(conflict.Value));
        Assert.Equal(Messages.IdentityConflict,
            conflict.Value.GetType().GetProperty("message")!.GetValue(conflict.Value));
    }

    [Fact]
    public async Task ConcurrentCanonicalRegistrationConflictReturnsConflictResult()
    {
        using var database = new TestDatabase();
        await using var winnerContext = database.CreateContext();
        var barrier = new BeforeSaveInterceptor();
        await using var loserContext = database.CreateContext(barrier);

        barrier.Action = async () => Assert.True((await Manager(winnerContext).RegisterAsync(new UserForRegisterDto
        {
            Email = "race@example.test",
            UserName = "RaceUser",
            FirstName = "Winner",
            LastName = "User",
            Password = WinningPassword
        })).Success);

        var result = await Manager(loserContext).RegisterAsync(new UserForRegisterDto
        {
            Email = "  RACE@EXAMPLE.TEST  ",
            UserName = "different-user",
            FirstName = "Loser",
            LastName = "User",
            Password = LosingPassword
        });

        Assert.IsAssignableFrom<TaskTracker.Core.Utilities.Results.IConflictResult>(result);
        await using var verificationContext = database.CreateContext();
        Assert.Single(await verificationContext.Users.Where(x => x.Email == "race@example.test").ToListAsync());
    }

    [Fact]
    public async Task EmailBasedAuthFlowsAcceptMixedCaseAndWhitespace()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var user = await MakeLegacyUser(context);

        var login = await Manager(context).LoginAsync(new UserForLoginDto
        {
            Email = "  USER1@EXAMPLE.TEST  ",
            Password = LegacyPassword
        });
        var forgot = await Manager(context).ForgotPasswordAsync(new ForgotPasswordDto
        {
            Email = "  USER1@EXAMPLE.TEST  "
        });

        Assert.True(login.Success);
        Assert.True(forgot.Success);
        Assert.Single(await context.PasswordResetRequests.Where(x => x.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task EmailVerificationAcceptsMixedCaseAndWhitespace()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var user = await context.Users.SingleAsync(x => x.Id == 1);
        user.IsVerified = false;
        context.OperationClaims.Add(new OperationClaim { Id = 2, Name = "User" });
        context.EmailVerifications.Add(new EmailVerification
        {
            UserId = user.Id,
            Code = "123456",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var result = await Manager(context).VerifyEmailAsync(new EmailVerificationDto
        {
            Email = "  USER1@EXAMPLE.TEST  ",
            Code = "123456"
        });

        Assert.True(result.Success);
        Assert.True(user.IsVerified);
    }

    [Fact]
    public async Task LegacyLoginUpgradesAndReturnsTokens()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var user = await MakeLegacyUser(context);

        var result = await Manager(context).LoginAsync(new UserForLoginDto
        {
            Email = user.Email,
            Password = LegacyPassword
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data.AccessToken.Token);
        Assert.NotEmpty(result.Data.RefreshToken);
        Assert.Equal(PasswordHashVersion.IdentityV3, user.PasswordHashVersion);
        Assert.Empty(user.PasswordSalt);
        Assert.Equal(PasswordVerificationOutcome.Valid, Hasher().Verify(user, LegacyPassword));
    }

    [Fact]
    public async Task WrongLegacyPasswordDoesNotMutateCredential()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var user = await MakeLegacyUser(context);
        var originalHash = user.PasswordHash.ToArray();
        var originalSalt = user.PasswordSalt.ToArray();

        var result = await Manager(context).LoginAsync(new UserForLoginDto
        {
            Email = user.Email,
            Password = "incorrect"
        });

        Assert.False(result.Success);
        Assert.Equal(PasswordHashVersion.LegacyHmacSha512, user.PasswordHashVersion);
        Assert.Equal(originalHash, user.PasswordHash);
        Assert.Equal(originalSalt, user.PasswordSalt);
        Assert.Empty(context.RefreshTokens.Where(x => x.UserId == user.Id));
    }

    [Fact]
    public async Task FailedUpgradeSaveReturnsNoSuccessAndLeavesLegacyCredentialRetryable()
    {
        using var database = new TestDatabase();
        await using (var seedContext = database.CreateContext())
            await MakeLegacyUser(seedContext);

        await using (var failingContext = database.CreateContext(new FailNextSaveInterceptor()))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Manager(failingContext).LoginAsync(new UserForLoginDto
            {
                Email = "user1@example.test",
                Password = LegacyPassword
            }));
        }

        await using var verificationContext = database.CreateContext();
        var stored = await verificationContext.Users.SingleAsync(x => x.Email == "user1@example.test");
        Assert.Equal(PasswordHashVersion.LegacyHmacSha512, stored.PasswordHashVersion);
        Assert.Equal(PasswordVerificationOutcome.ValidNeedsUpgrade, Hasher().Verify(stored, LegacyPassword));
        Assert.Empty(verificationContext.RefreshTokens.Where(x => x.UserId == stored.Id));
    }

    [Fact]
    public async Task PasswordResetWritesIdentityV3()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var user = await MakeLegacyUser(context);
        var rawToken = "reset-token";
        context.PasswordResetRequests.Add(new PasswordResetRequest
        {
            UserId = user.Id,
            CodeHash = "code-hash",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            VerifiedAt = DateTime.UtcNow,
            ResetTokenHash = PasswordResetTokenGenerator.HashToken(rawToken),
            ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await context.SaveChangesAsync();

        var result = await Manager(context).ResetPasswordAsync(new ResetPasswordDto
        {
            ResetToken = rawToken,
            NewPassword = NewPassword,
            ConfirmNewPassword = NewPassword
        });

        Assert.True(result.Success);
        Assert.Equal(PasswordHashVersion.IdentityV3, user.PasswordHashVersion);
        Assert.Equal(PasswordVerificationOutcome.Valid, Hasher().Verify(user, NewPassword));
    }

    [Fact]
    public async Task PasswordChangeWritesIdentityV3()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var user = await MakeLegacyUser(context);

        var result = await Manager(context, user.Id).ChangePasswordAsync(new ChangePasswordDto
        {
            CurrentPassword = LegacyPassword,
            NewPassword = NewPassword,
            ConfirmNewPassword = NewPassword
        });

        Assert.True(result.Success);
        Assert.Equal(PasswordHashVersion.IdentityV3, user.PasswordHashVersion);
        Assert.Equal(PasswordVerificationOutcome.Valid, Hasher().Verify(user, NewPassword));
        Assert.Equal(PasswordVerificationOutcome.Failed, Hasher().Verify(user, LegacyPassword));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task IneligibleUserIsNotUpgraded(bool verified, bool status)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var user = await MakeLegacyUser(context);
        user.IsVerified = verified;
        user.Status = status;
        await context.SaveChangesAsync();

        var result = await Manager(context).LoginAsync(new UserForLoginDto
        {
            Email = user.Email,
            Password = LegacyPassword
        });

        Assert.False(result.Success);
        Assert.Equal(PasswordHashVersion.LegacyHmacSha512, user.PasswordHashVersion);
    }

    [Fact]
    public async Task ConcurrentLegacyLoginsUpgradeWithoutCorruptingCredential()
    {
        using var database = new TestDatabase();
        await using (var seedContext = database.CreateContext())
            await MakeLegacyUser(seedContext);

        await using var winnerContext = database.CreateContext();
        var barrier = new BeforeSaveInterceptor();
        await using var loserContext = database.CreateContext(barrier);
        var dto = new UserForLoginDto { Email = "user1@example.test", Password = LegacyPassword };
        barrier.Action = async () => Assert.True((await Manager(winnerContext).LoginAsync(dto)).Success);

        var loserResult = await Manager(loserContext).LoginAsync(dto);

        Assert.True(loserResult.Success);
        await using var verificationContext = database.CreateContext();
        var stored = await verificationContext.Users.SingleAsync(x => x.Id == 1);
        Assert.Equal(PasswordHashVersion.IdentityV3, stored.PasswordHashVersion);
        Assert.Equal(PasswordVerificationOutcome.Valid, Hasher().Verify(stored, LegacyPassword));
        Assert.Equal(2, await verificationContext.RefreshTokens.CountAsync(x => x.UserId == stored.Id));
    }

    [Fact]
    public async Task LegacyLoginRacingResetCannotRestoreOldPassword()
    {
        using var database = new TestDatabase();
        const string rawToken = "racing-reset-token";
        await using var winnerContext = database.CreateContext();
        var user = await MakeLegacyUser(winnerContext);
        winnerContext.PasswordResetRequests.Add(new PasswordResetRequest
        {
            UserId = user.Id,
            CodeHash = "code-hash",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            VerifiedAt = DateTime.UtcNow,
            ResetTokenHash = PasswordResetTokenGenerator.HashToken(rawToken),
            ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await winnerContext.SaveChangesAsync();

        var barrier = new BeforeSaveInterceptor();
        await using var loginContext = database.CreateContext(barrier);
        barrier.Action = async () => Assert.True((await Manager(winnerContext).ResetPasswordAsync(new ResetPasswordDto
        {
            ResetToken = rawToken,
            NewPassword = NewPassword,
            ConfirmNewPassword = NewPassword
        })).Success);

        var login = await Manager(loginContext).LoginAsync(new UserForLoginDto
        {
            Email = user.Email,
            Password = LegacyPassword
        });

        Assert.False(login.Success);
        await using var verificationContext = database.CreateContext();
        var stored = await verificationContext.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(PasswordVerificationOutcome.Valid, Hasher().Verify(stored, NewPassword));
        Assert.Equal(PasswordVerificationOutcome.Failed, Hasher().Verify(stored, LegacyPassword));
        Assert.Empty(await verificationContext.RefreshTokens.Where(x => x.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task LegacyLoginRacingChangeCannotRestoreOldPassword()
    {
        using var database = new TestDatabase();
        await using var winnerContext = database.CreateContext();
        var user = await MakeLegacyUser(winnerContext);
        var barrier = new BeforeSaveInterceptor();
        await using var loginContext = database.CreateContext(barrier);
        barrier.Action = async () => Assert.True((await Manager(winnerContext, user.Id).ChangePasswordAsync(
            new ChangePasswordDto
            {
                CurrentPassword = LegacyPassword,
                NewPassword = NewPassword,
                ConfirmNewPassword = NewPassword
            })).Success);

        var login = await Manager(loginContext).LoginAsync(new UserForLoginDto
        {
            Email = user.Email,
            Password = LegacyPassword
        });

        Assert.False(login.Success);
        await using var verificationContext = database.CreateContext();
        var stored = await verificationContext.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(PasswordVerificationOutcome.Valid, Hasher().Verify(stored, NewPassword));
        Assert.Equal(PasswordVerificationOutcome.Failed, Hasher().Verify(stored, LegacyPassword));
        Assert.Empty(await verificationContext.RefreshTokens.Where(x => x.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentPasswordChangesRejectStaleWriter()
    {
        using var database = new TestDatabase();
        await using var winnerContext = database.CreateContext();
        var user = await MakeLegacyUser(winnerContext);
        var barrier = new BeforeSaveInterceptor();
        await using var staleContext = database.CreateContext(barrier);
        barrier.Action = async () => Assert.True((await Manager(winnerContext, user.Id).ChangePasswordAsync(
            new ChangePasswordDto
            {
                CurrentPassword = LegacyPassword,
                NewPassword = WinningPassword,
                ConfirmNewPassword = WinningPassword
            })).Success);

        var staleResult = await Manager(staleContext, user.Id).ChangePasswordAsync(new ChangePasswordDto
        {
            CurrentPassword = LegacyPassword,
            NewPassword = LosingPassword,
            ConfirmNewPassword = LosingPassword
        });

        Assert.False(staleResult.Success);
        Assert.Equal(Messages.PasswordCredentialChangedConcurrently, staleResult.Message);
        await using var verificationContext = database.CreateContext();
        var stored = await verificationContext.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(PasswordHashVersion.IdentityV3, stored.PasswordHashVersion);
        Assert.NotEmpty(stored.PasswordHash);
        Assert.Empty(stored.PasswordSalt);
        Assert.Equal(PasswordVerificationOutcome.Valid, Hasher().Verify(stored, WinningPassword));
        Assert.Equal(PasswordVerificationOutcome.Failed, Hasher().Verify(stored, LosingPassword));
    }

    [Fact]
    public async Task ConcurrentPasswordResetsRejectStaleWriterAndRollbackItsMutations()
    {
        using var database = new TestDatabase();
        const string staleRawToken = "stale-reset-token";
        const string winningRawToken = "winning-reset-token";
        await using var winnerContext = database.CreateContext();
        var user = await MakeLegacyUser(winnerContext);
        var staleRequest = ResetRequest(user.Id, staleRawToken);
        var winningRequest = ResetRequest(user.Id, winningRawToken);
        winnerContext.PasswordResetRequests.AddRange(staleRequest, winningRequest);
        winnerContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = "active-refresh-token",
            Expires = DateTime.UtcNow.AddDays(1)
        });
        await winnerContext.SaveChangesAsync();

        var barrier = new BeforeSaveInterceptor();
        await using var staleContext = database.CreateContext(barrier);
        barrier.Action = async () => Assert.True((await Manager(winnerContext).ResetPasswordAsync(
            new ResetPasswordDto
            {
                ResetToken = winningRawToken,
                NewPassword = WinningPassword,
                ConfirmNewPassword = WinningPassword
            })).Success);

        var staleResult = await Manager(staleContext).ResetPasswordAsync(new ResetPasswordDto
        {
            ResetToken = staleRawToken,
            NewPassword = LosingPassword,
            ConfirmNewPassword = LosingPassword
        });

        Assert.False(staleResult.Success);
        Assert.Equal(Messages.PasswordCredentialChangedConcurrently, staleResult.Message);
        await using var verificationContext = database.CreateContext();
        var stored = await verificationContext.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(PasswordHashVersion.IdentityV3, stored.PasswordHashVersion);
        Assert.NotEmpty(stored.PasswordHash);
        Assert.Empty(stored.PasswordSalt);
        Assert.Equal(PasswordVerificationOutcome.Valid, Hasher().Verify(stored, WinningPassword));
        Assert.Equal(PasswordVerificationOutcome.Failed, Hasher().Verify(stored, LosingPassword));

        var storedStaleRequest = await verificationContext.PasswordResetRequests.SingleAsync(x => x.Id == staleRequest.Id);
        var storedWinningRequest = await verificationContext.PasswordResetRequests.SingleAsync(x => x.Id == winningRequest.Id);
        Assert.Null(storedStaleRequest.UsedAt);
        Assert.NotNull(storedStaleRequest.InvalidatedAt);
        Assert.NotNull(storedWinningRequest.UsedAt);
        Assert.Null(storedWinningRequest.InvalidatedAt);
        Assert.True((await verificationContext.RefreshTokens.SingleAsync(x => x.UserId == user.Id)).IsRevoked);
    }

    private static PasswordResetRequest ResetRequest(int userId, string rawToken) => new()
    {
        UserId = userId,
        CodeHash = $"code-{rawToken}",
        ExpiresAt = DateTime.UtcNow.AddMinutes(10),
        VerifiedAt = DateTime.UtcNow,
        ResetTokenHash = PasswordResetTokenGenerator.HashToken(rawToken),
        ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(10)
    };

    private static async Task<User> MakeLegacyUser(TaskTrackerDbContext context)
    {
        var user = await context.Users.SingleAsync(x => x.Id == 1);
        HashingHelper.CreatePasswordHash(LegacyPassword, out var hash, out var salt);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.PasswordHashVersion = PasswordHashVersion.LegacyHmacSha512;
        user.IsVerified = true;
        user.Status = true;
        await context.SaveChangesAsync();
        return user;
    }

    private static PasswordHashService Hasher() => PasswordHashServiceTests.Service();

    private static AuthManager Manager(TaskTrackerDbContext context, int currentUserId = 1) => new(
        new UnitOfWork(context),
        new Tokens(),
        new Email(),
        new CurrentUser(currentUserId),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PasswordRecovery:HmacSecret"] = new string('s', 32)
        }).Build(),
        NullLogger<AuthManager>.Instance,
        Hasher(),
        new IdentityNormalizer());

    private sealed class CurrentUser(int id) : ICurrentUserService
    {
        public int UserId => id;
    }

    private sealed class Email : IEmailService
    {
        public Task SendVerificationCodeAsync(string email, string code) => Task.CompletedTask;
        public Task SendPasswordResetCodeAsync(string email, string code) => Task.CompletedTask;
        public Task SendTaskShareInvitationEmailAsync(string email, string taskTitle, string inviterUsername,
            string invitationUrl) => Task.CompletedTask;
    }

    private sealed class Tokens : ITokenHelper
    {
        public AccessToken CreateToken(User user, List<OperationClaim> operationClaims) => new()
        {
            Token = $"access-{user.Id}",
            Expiration = DateTime.UtcNow.AddMinutes(30)
        };

        public RefreshToken CreateRefreshToken(int userId) => new()
        {
            UserId = userId,
            Token = Guid.NewGuid().ToString("N"),
            Expires = DateTime.UtcNow.AddDays(7)
        };
    }

    private sealed class FailNextSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(new DbUpdateException("Simulated save failure."));
    }

    private sealed class BeforeSaveInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? Action { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Action is not null)
            {
                var action = Action;
                Action = null;
                await action();
            }

            return result;
        }
    }
}
