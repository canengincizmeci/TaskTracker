using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Security.Cryptography;
using TaskTracker.Core.Utilities.Security.Hashing;
using TaskTracker.Core.Utilities.Security.Jwt;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class PostgreSqlPasswordHardeningTests
{
    private const string WinningPassword = "winning password!";
    private const string LosingPassword = "losing password!";

    [PostgreSqlFact]
    public async Task EmptyByteaAndPasswordHashConcurrencyAreAtomic()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var winningContext = database.CreateContext();
        await using var staleContext = database.CreateContext();
        var winningUser = await winningContext.Users.SingleAsync(x => x.Id == 1);
        var staleUser = await staleContext.Users.SingleAsync(x => x.Id == 1);
        var hasher = PasswordHashServiceTests.Service();

        PasswordHashServiceTests.Apply(winningUser, hasher.CreateHash(winningUser, WinningPassword));
        await winningContext.SaveChangesAsync();

        PasswordHashServiceTests.Apply(staleUser, hasher.CreateHash(staleUser, LosingPassword));
        staleContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = staleUser.Id,
            Token = "must-roll-back",
            Expires = DateTime.UtcNow.AddDays(1)
        });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());

        await using var verificationContext = database.CreateContext();
        var stored = await verificationContext.Users.SingleAsync(x => x.Id == 1);
        Assert.Equal(PasswordHashVersion.IdentityV3, stored.PasswordHashVersion);
        Assert.NotEmpty(stored.PasswordHash);
        Assert.Empty(stored.PasswordSalt);
        Assert.Equal(PasswordVerificationOutcome.Valid, hasher.Verify(stored, WinningPassword));
        Assert.Equal(PasswordVerificationOutcome.Failed, hasher.Verify(stored, LosingPassword));
        Assert.Empty(await verificationContext.RefreshTokens.Where(x => x.UserId == stored.Id).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task CanonicalIdentityColumnsHaveDatabaseBackedUniqueness()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();

        await using (var userNameContext = database.CreateContext())
        {
            userNameContext.Users.Add(CreateUser("DisplayOne", "shared-key", "one@example.test"));
            await userNameContext.SaveChangesAsync();
        }

        await using (var duplicateUserNameContext = database.CreateContext())
        {
            duplicateUserNameContext.Users.Add(CreateUser("DISPLAYONE", "shared-key", "two@example.test"));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateUserNameContext.SaveChangesAsync());
        }

        await using (var duplicateEmailContext = database.CreateContext())
        {
            duplicateEmailContext.Users.Add(CreateUser("Other", "other", "one@example.test"));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateEmailContext.SaveChangesAsync());
        }
    }

    [PostgreSqlFact]
    public async Task UserAndVerificationInsertRollbackTogetherWhenVerificationIsInvalid()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var user = CreateUser("AtomicRegistration", "atomicregistration", "atomic@example.test");

        await using (var failingContext = database.CreateContext())
        {
            failingContext.Users.Add(user);
            failingContext.EmailVerifications.Add(new EmailVerification
            {
                User = user,
                Code = null!
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => failingContext.SaveChangesAsync());
        }

        await using var verificationContext = database.CreateContext();
        Assert.DoesNotContain(await verificationContext.Users.ToListAsync(),
            x => x.Email == "atomic@example.test");
        Assert.DoesNotContain(await verificationContext.EmailVerifications.ToListAsync(),
            x => x.User.Email == "atomic@example.test");
    }

    [PostgreSqlFact]
    public async Task ConcurrentResendsOnExistingRowHaveOneWinner()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedVerificationAsync(database, "111111");
        var winnerEmail = new Email();
        var loserEmail = new Email();
        await using var winnerContext = database.CreateContext();
        var beforeLoserSave = new BeforeSaveInterceptor();
        await using var loserContext = database.CreateContext(beforeLoserSave);
        beforeLoserSave.Action = async () =>
        {
            var winner = await Manager(winnerContext, winnerEmail)
                .ResendVerificationAsync(new ResendVerificationDto { Email = "pg-user1@example.test" });
            Assert.True(winner.Success);
        };

        var loser = await Manager(loserContext, loserEmail)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "pg-user1@example.test" });

        Assert.True(loser.Success);
        Assert.Single(winnerEmail.VerificationAttempts);
        Assert.Empty(loserEmail.VerificationAttempts);
        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.EmailVerifications.SingleAsync(x => x.UserId == 1 && !x.IsVerified);
        Assert.Equal(winnerEmail.VerificationAttempts[0].Code, persisted.Code);
    }

    [PostgreSqlFact]
    public async Task ResendPersistedBeforeNewerResendCannotSendItsSupersededCode()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedVerificationAsync(database, "111111", seedClaim: true);
        string? supersededCode = null;
        var firstEmail = new Email();
        var newerEmail = new Email();
        var afterFirstPersistence = new AfterSaveInterceptor();
        await using var firstContext = database.CreateContext(afterFirstPersistence);
        afterFirstPersistence.Action = async () =>
        {
            await using (var inspectionContext = database.CreateContext())
            {
                supersededCode = (await inspectionContext.EmailVerifications
                    .SingleAsync(x => x.UserId == 1 && !x.IsVerified)).Code;
            }

            await using var newerContext = database.CreateContext();
            var newer = await Manager(newerContext, newerEmail)
                .ResendVerificationAsync(new ResendVerificationDto { Email = "pg-user1@example.test" });
            Assert.True(newer.Success);
        };

        var first = await Manager(firstContext, firstEmail)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "pg-user1@example.test" });

        Assert.True(first.Success);
        Assert.NotNull(supersededCode);
        Assert.Empty(firstEmail.VerificationAttempts);
        Assert.Single(newerEmail.VerificationAttempts);
        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.EmailVerifications.SingleAsync(x => x.UserId == 1 && !x.IsVerified);
        Assert.NotEqual(supersededCode, persisted.Code);
        Assert.Equal(newerEmail.VerificationAttempts[0].Code, persisted.Code);
        Assert.Null(persisted.DeliveryToken);
        Assert.False(persisted.DeliveryClaimed);

        var staleVerification = await Manager(verificationContext, new Email())
            .VerifyEmailAsync(new EmailVerificationDto
            {
                Email = "pg-user1@example.test",
                Code = supersededCode!
            });
        Assert.False(staleVerification.Success);
    }

    [PostgreSqlFact]
    public async Task ConcurrentResendsWithoutExistingRowCreateOneUsableState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var winnerEmail = new Email();
        var loserEmail = new Email();
        await using var winnerContext = database.CreateContext();
        var beforeLoserSave = new BeforeSaveInterceptor();
        await using var loserContext = database.CreateContext(beforeLoserSave);
        beforeLoserSave.Action = async () =>
        {
            var winner = await Manager(winnerContext, winnerEmail)
                .ResendVerificationAsync(new ResendVerificationDto { Email = "pg-user1@example.test" });
            Assert.True(winner.Success);
        };

        var loser = await Manager(loserContext, loserEmail)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "pg-user1@example.test" });

        Assert.True(loser.Success);
        Assert.Single(winnerEmail.VerificationAttempts);
        Assert.Empty(loserEmail.VerificationAttempts);
        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.EmailVerifications.SingleAsync(x => x.UserId == 1 && !x.IsVerified);
        Assert.Equal(winnerEmail.VerificationAttempts[0].Code, persisted.Code);
    }

    [PostgreSqlFact]
    public async Task VerifyWinningAgainstResendCannotBeReactivated()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedVerificationAsync(database, "123456", seedClaim: true);
        var resendEmail = new Email();
        await using var verifyContext = database.CreateContext();
        var beforeResendSave = new BeforeSaveInterceptor();
        await using var resendContext = database.CreateContext(beforeResendSave);
        beforeResendSave.Action = async () =>
        {
            var verified = await Manager(verifyContext, new Email()).VerifyEmailAsync(new EmailVerificationDto
            {
                Email = "pg-user1@example.test",
                Code = "123456"
            });
            Assert.True(verified.Success);
        };

        var resend = await Manager(resendContext, resendEmail)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "pg-user1@example.test" });

        Assert.True(resend.Success);
        Assert.Empty(resendEmail.VerificationAttempts);
        await using var verificationContext = database.CreateContext();
        Assert.True((await verificationContext.Users.SingleAsync(x => x.Id == 1)).IsVerified);
        var persisted = await verificationContext.EmailVerifications.SingleAsync(x => x.UserId == 1);
        Assert.True(persisted.IsVerified);
        Assert.Null(persisted.DeliveryToken);
        Assert.False(persisted.DeliveryClaimed);
    }

    [PostgreSqlFact]
    public async Task ResendWinningAgainstVerifyInvalidatesOldCode()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await SeedVerificationAsync(database, "123456", seedClaim: true);
        var resendEmail = new Email();
        await using var resendContext = database.CreateContext();
        var beforeVerifySave = new BeforeSaveInterceptor();
        await using var verifyContext = database.CreateContext(beforeVerifySave);
        beforeVerifySave.Action = async () =>
        {
            var resend = await Manager(resendContext, resendEmail)
                .ResendVerificationAsync(new ResendVerificationDto { Email = "pg-user1@example.test" });
            Assert.True(resend.Success);
        };

        var staleVerify = await Manager(verifyContext, new Email()).VerifyEmailAsync(new EmailVerificationDto
        {
            Email = "pg-user1@example.test",
            Code = "123456"
        });

        Assert.False(staleVerify.Success);
        Assert.Single(resendEmail.VerificationAttempts);
        await using var verificationContext = database.CreateContext();
        Assert.False((await verificationContext.Users.SingleAsync(x => x.Id == 1)).IsVerified);
        var persisted = await verificationContext.EmailVerifications.SingleAsync(x => x.UserId == 1 && !x.IsVerified);
        Assert.Equal(resendEmail.VerificationAttempts[0].Code, persisted.Code);
    }

    [Fact]
    public void UserModelUsesOnlyTheNormalizedUserNameAsItsUniqueUserNameKey()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var entity = context.Model.FindEntityType(typeof(User))!;

        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(User.NormalizedUserName));
        Assert.DoesNotContain(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(User.UserName));
    }

    private static User CreateUser(string userName, string normalizedUserName, string email) => new()
    {
        FirstName = "Identity",
        LastName = "Test",
        UserName = userName,
        NormalizedUserName = normalizedUserName,
        Email = email,
        PasswordHash = [1],
        PasswordSalt = [1],
        Status = true
    };

    private static async Task SeedVerificationAsync(
        PostgreSqlTestDatabase database,
        string code,
        bool seedClaim = false)
    {
        await using var context = database.CreateContext();
        if (seedClaim && !await context.OperationClaims.AnyAsync(x => x.Id == 2))
            context.OperationClaims.Add(new OperationClaim { Id = 2, Name = "User" });
        context.EmailVerifications.Add(new EmailVerification
        {
            UserId = 1,
            Code = code,
            CreatedAt = DateTime.UtcNow,
            DeliveryToken = seedClaim ? Guid.NewGuid() : null
        });
        await context.SaveChangesAsync();
    }

    private static AuthManager Manager(TaskTrackerDbContext context, IEmailService emailService) => new(
        new UnitOfWork(context),
        new Tokens(),
        emailService,
        new CurrentUser(),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PasswordRecovery:HmacSecret"] = new string('s', 32)
        }).Build(),
        NullLogger<AuthManager>.Instance,
        PasswordHashServiceTests.Service(),
        new IdentityNormalizer());

    private sealed class CurrentUser : ICurrentUserService
    {
        public int UserId => 1;
    }

    private sealed class Email : IEmailService
    {
        public List<(string Email, string Code)> VerificationAttempts { get; } = [];

        public Task SendVerificationCodeAsync(string email, string code)
        {
            VerificationAttempts.Add((email, code));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetCodeAsync(string email, string code) => Task.CompletedTask;
        public Task SendTaskShareInvitationEmailAsync(string email, string taskTitle, string inviterUsername,
            string invitationUrl) => Task.CompletedTask;
    }

    private sealed class Tokens : ITokenHelper
    {
        public AccessToken CreateToken(User user, List<OperationClaim> operationClaims) => new();

        public RefreshToken CreateRefreshToken(int userId) => new()
        {
            UserId = userId,
            Token = Guid.NewGuid().ToString("N"),
            Expires = DateTime.UtcNow.AddDays(7)
        };
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

    private sealed class AfterSaveInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? Action { get; set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
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
