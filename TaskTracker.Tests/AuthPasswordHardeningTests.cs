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
        Assert.Single(await context.EmailVerifications.Where(x => x.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task RegistrationSaveFailureLeavesNoPartialIdentityData()
    {
        using var database = new TestDatabase();
        await using (var failingContext = database.CreateContext(new FailNextSaveInterceptor()))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                Manager(failingContext).RegisterAsync(Registration("atomic@example.test", "atomic-user")));
        }

        await using var verificationContext = database.CreateContext();
        Assert.DoesNotContain(await verificationContext.Users.ToListAsync(),
            x => x.Email == "atomic@example.test");
        Assert.Empty(await verificationContext.EmailVerifications.ToListAsync());
    }

    [Fact]
    public async Task VerificationInsertFailureRollsBackTheUserInsert()
    {
        using var database = new TestDatabase();
        await using (var failingContext = database.CreateContext(new BreakVerificationSaveInterceptor()))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                Manager(failingContext).RegisterAsync(Registration("rollback@example.test", "rollback-user")));
        }

        await using var verificationContext = database.CreateContext();
        Assert.DoesNotContain(await verificationContext.Users.ToListAsync(),
            x => x.Email == "rollback@example.test");
        Assert.Empty(await verificationContext.EmailVerifications.ToListAsync());
    }

    [Fact]
    public async Task DeliveryFailureKeepsRegistrationRecoverableAndMapsToAcceptedResponse()
    {
        using var database = new TestDatabase();
        var email = new Email { FailVerificationDelivery = true };
        await using var context = database.CreateContext();
        var controller = new AuthController(Manager(context, emailService: email));

        var response = await controller.Register(Registration("delivery@example.test", "delivery-user"));

        var unavailable = Assert.IsType<AcceptedResult>(response);
        Assert.Equal(202, unavailable.StatusCode);
        Assert.Equal("verification_delivery_failed",
            unavailable.Value!.GetType().GetProperty("code")!.GetValue(unavailable.Value));
        var user = await context.Users.SingleAsync(x => x.Email == "delivery@example.test");
        var originalVerification = await context.EmailVerifications.SingleAsync(x => x.UserId == user.Id);

        email.FailVerificationDelivery = false;
        var resend = await Manager(context, emailService: email).ResendVerificationAsync(new ResendVerificationDto
        {
            Email = "  DELIVERY@EXAMPLE.TEST  "
        });

        Assert.True(resend.Success);
        Assert.Equal(Messages.VerificationResendGeneric, resend.Message);
        Assert.Equal(2, email.VerificationAttempts.Count);
        Assert.NotEqual(email.VerificationAttempts[0].Code, email.VerificationAttempts[1].Code);
        Assert.Equal(email.VerificationAttempts[1].Code, originalVerification.Code);
        Assert.Null(originalVerification.DeliveryToken);
        Assert.False(originalVerification.DeliveryClaimed);
        Assert.Single(await context.EmailVerifications.Where(x => x.UserId == user.Id && !x.IsVerified).ToListAsync());
    }

    [Fact]
    public async Task ResendReplacesActiveCodeWithoutChangingTheUserOrCreatingAmbiguity()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var user = await context.Users.SingleAsync(x => x.Id == 1);
        await context.Database.ExecuteSqlRawAsync(
            "DROP INDEX \"UX_EmailVerifications_UserId_Active\"");
        var originalHash = user.PasswordHash.ToArray();
        var originalSalt = user.PasswordSalt.ToArray();
        var older = new EmailVerification
        {
            UserId = user.Id,
            Code = "111111",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };
        var current = new EmailVerification
        {
            UserId = user.Id,
            Code = "222222",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            FailedAttemptCount = 3,
            LockedUntil = DateTime.UtcNow.AddMinutes(1)
        };
        context.EmailVerifications.AddRange(older, current);
        await context.SaveChangesAsync();
        var email = new Email();

        var first = await Manager(context, emailService: email).ResendVerificationAsync(new ResendVerificationDto
        {
            Email = "  USER1@EXAMPLE.TEST  "
        });
        var firstCode = current.Code;
        var second = await Manager(context, emailService: email).ResendVerificationAsync(new ResendVerificationDto
        {
            Email = "user1@example.test"
        });

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(Messages.VerificationResendGeneric, first.Message);
        Assert.True(older.IsVerified);
        Assert.False(current.IsVerified);
        Assert.NotEqual("222222", firstCode);
        Assert.NotEqual(firstCode, current.Code);
        Assert.Equal(0, current.FailedAttemptCount);
        Assert.Null(current.LockedUntil);
        Assert.Equal(2, await context.EmailVerifications.CountAsync(x => x.UserId == user.Id));
        Assert.Single(await context.EmailVerifications.Where(x => x.UserId == user.Id && !x.IsVerified).ToListAsync());
        Assert.Equal(2, email.VerificationAttempts.Count);
        Assert.Equal(current.Code, email.VerificationAttempts[1].Code);
        Assert.Equal("Test", user.FirstName);
        Assert.Equal("User", user.LastName);
        Assert.Equal("user1", user.UserName);
        Assert.Equal(originalHash, user.PasswordHash);
        Assert.Equal(originalSalt, user.PasswordSalt);
        Assert.Null(current.DeliveryToken);
        Assert.False(current.DeliveryClaimed);
    }

    [Fact]
    public async Task ResendReturnsTheSameGenericResponseForVerifiedAndUnknownAccounts()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var verifiedUser = await context.Users.SingleAsync(x => x.Id == 1);
        verifiedUser.IsVerified = true;
        await context.SaveChangesAsync();
        var email = new Email();
        var manager = Manager(context, emailService: email);

        var verified = await manager.ResendVerificationAsync(new ResendVerificationDto
        {
            Email = " USER1@EXAMPLE.TEST "
        });
        var unknown = await manager.ResendVerificationAsync(new ResendVerificationDto
        {
            Email = "unknown@example.test"
        });

        Assert.True(verified.Success);
        Assert.True(unknown.Success);
        Assert.Equal(Messages.VerificationResendGeneric, verified.Message);
        Assert.Equal(verified.Message, unknown.Message);
        Assert.Empty(email.VerificationAttempts);
    }

    [Fact]
    public async Task ConcurrentResendsOnExistingRowHaveOneWinnerAndOnlyWinnerSends()
    {
        using var database = new TestDatabase();
        await using (var setup = database.CreateContext())
        {
            setup.EmailVerifications.Add(new EmailVerification
            {
                UserId = 1,
                Code = "111111",
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        var winnerEmail = new Email();
        var loserEmail = new Email();
        await using var winnerContext = database.CreateContext();
        var beforeLoserSave = new BeforeSaveInterceptor();
        await using var loserContext = database.CreateContext(beforeLoserSave);
        beforeLoserSave.Action = async () =>
        {
            var winner = await Manager(winnerContext, emailService: winnerEmail)
                .ResendVerificationAsync(new ResendVerificationDto { Email = "user1@example.test" });
            Assert.True(winner.Success);
        };

        var loser = await Manager(loserContext, emailService: loserEmail)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "user1@example.test" });

        Assert.True(loser.Success);
        Assert.Single(winnerEmail.VerificationAttempts);
        Assert.Empty(loserEmail.VerificationAttempts);
        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.EmailVerifications.SingleAsync(x => x.UserId == 1 && !x.IsVerified);
        Assert.Equal(winnerEmail.VerificationAttempts[0].Code, persisted.Code);
        Assert.Null(persisted.DeliveryToken);
        Assert.False(persisted.DeliveryClaimed);
    }

    [Fact]
    public async Task ResendPersistedBeforeNewerResendCannotSendItsSupersededCode()
    {
        using var database = new TestDatabase();
        await using (var setup = database.CreateContext())
        {
            setup.OperationClaims.Add(new OperationClaim { Id = 2, Name = "User" });
            setup.EmailVerifications.Add(new EmailVerification
            {
                UserId = 1,
                Code = "111111",
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

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
            var newer = await Manager(newerContext, emailService: newerEmail)
                .ResendVerificationAsync(new ResendVerificationDto { Email = "user1@example.test" });
            Assert.True(newer.Success);
        };

        var first = await Manager(firstContext, emailService: firstEmail)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "user1@example.test" });

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

        var staleVerification = await Manager(verificationContext).VerifyEmailAsync(new EmailVerificationDto
        {
            Email = "user1@example.test",
            Code = supersededCode!
        });
        Assert.False(staleVerification.Success);
    }

    [Fact]
    public async Task ConcurrentResendsWithoutExistingRowCreateOneUsableState()
    {
        using var database = new TestDatabase();
        var winnerEmail = new Email();
        var loserEmail = new Email();
        await using var winnerContext = database.CreateContext();
        var beforeLoserSave = new BeforeSaveInterceptor();
        await using var loserContext = database.CreateContext(beforeLoserSave);
        beforeLoserSave.Action = async () =>
        {
            var winner = await Manager(winnerContext, emailService: winnerEmail)
                .ResendVerificationAsync(new ResendVerificationDto { Email = "user1@example.test" });
            Assert.True(winner.Success);
        };

        var loser = await Manager(loserContext, emailService: loserEmail)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "user1@example.test" });

        Assert.True(loser.Success);
        Assert.Single(winnerEmail.VerificationAttempts);
        Assert.Empty(loserEmail.VerificationAttempts);
        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.EmailVerifications.SingleAsync(x => x.UserId == 1 && !x.IsVerified);
        Assert.Equal(winnerEmail.VerificationAttempts[0].Code, persisted.Code);
        Assert.Null(persisted.DeliveryToken);
        Assert.False(persisted.DeliveryClaimed);
    }

    [Fact]
    public async Task VerifyWinningAgainstResendCannotBeReactivatedOrSendANewCode()
    {
        using var database = new TestDatabase();
        await using (var setup = database.CreateContext())
        {
            setup.OperationClaims.Add(new OperationClaim { Id = 2, Name = "User" });
            setup.EmailVerifications.Add(new EmailVerification
            {
                UserId = 1,
                Code = "123456",
                CreatedAt = DateTime.UtcNow,
                DeliveryToken = Guid.NewGuid()
            });
            await setup.SaveChangesAsync();
        }

        var resendEmail = new Email();
        await using var verifyContext = database.CreateContext();
        var beforeResendSave = new BeforeSaveInterceptor();
        await using var resendContext = database.CreateContext(beforeResendSave);
        beforeResendSave.Action = async () =>
        {
            var verified = await Manager(verifyContext).VerifyEmailAsync(new EmailVerificationDto
            {
                Email = "user1@example.test",
                Code = "123456"
            });
            Assert.True(verified.Success);
        };

        var resend = await Manager(resendContext, emailService: resendEmail)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "user1@example.test" });

        Assert.True(resend.Success);
        Assert.Empty(resendEmail.VerificationAttempts);
        await using var verificationContext = database.CreateContext();
        Assert.True((await verificationContext.Users.SingleAsync(x => x.Id == 1)).IsVerified);
        var persisted = await verificationContext.EmailVerifications.SingleAsync(x => x.UserId == 1);
        Assert.True(persisted.IsVerified);
        Assert.Null(persisted.DeliveryToken);
        Assert.False(persisted.DeliveryClaimed);
    }

    [Fact]
    public async Task ResendWinningAgainstVerifyInvalidatesTheOldCode()
    {
        using var database = new TestDatabase();
        await using (var setup = database.CreateContext())
        {
            setup.OperationClaims.Add(new OperationClaim { Id = 2, Name = "User" });
            setup.EmailVerifications.Add(new EmailVerification
            {
                UserId = 1,
                Code = "123456",
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        var resendEmail = new Email();
        await using var resendContext = database.CreateContext();
        var beforeVerifySave = new BeforeSaveInterceptor();
        await using var verifyContext = database.CreateContext(beforeVerifySave);
        beforeVerifySave.Action = async () =>
        {
            var resend = await Manager(resendContext, emailService: resendEmail)
                .ResendVerificationAsync(new ResendVerificationDto { Email = "user1@example.test" });
            Assert.True(resend.Success);
        };

        var staleVerify = await Manager(verifyContext).VerifyEmailAsync(new EmailVerificationDto
        {
            Email = "user1@example.test",
            Code = "123456"
        });

        Assert.False(staleVerify.Success);
        Assert.Single(resendEmail.VerificationAttempts);
        await using var verificationContext = database.CreateContext();
        Assert.False((await verificationContext.Users.SingleAsync(x => x.Id == 1)).IsVerified);
        var persisted = await verificationContext.EmailVerifications.SingleAsync(x => x.UserId == 1 && !x.IsVerified);
        Assert.NotEqual("123456", persisted.Code);
        Assert.Equal(resendEmail.VerificationAttempts[0].Code, persisted.Code);
    }

    [Fact]
    public async Task ResendDeliveryFailureKeepsGenericResponseAndCoherentState()
    {
        using var database = new TestDatabase();
        await using (var setup = database.CreateContext())
        {
            setup.EmailVerifications.Add(new EmailVerification
            {
                UserId = 1,
                Code = "123456",
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        var email = new Email { FailVerificationDelivery = true };
        await using var failedContext = database.CreateContext();
        var failedDelivery = await Manager(failedContext, emailService: email)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "user1@example.test" });
        var unknown = await Manager(failedContext, emailService: email)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "unknown@example.test" });

        Assert.True(failedDelivery.Success);
        Assert.Equal(Messages.VerificationResendGeneric, failedDelivery.Message);
        Assert.Equal(failedDelivery.Message, unknown.Message);
        Assert.Single(email.VerificationAttempts);
        var failedCode = email.VerificationAttempts[0].Code;
        await using (var failedVerificationContext = database.CreateContext())
        {
            var failedState = await failedVerificationContext.EmailVerifications
                .SingleAsync(x => x.UserId == 1 && !x.IsVerified);
            Assert.Equal(failedCode, failedState.Code);
            Assert.Null(failedState.DeliveryToken);
            Assert.False(failedState.DeliveryClaimed);
        }

        email.FailVerificationDelivery = false;
        await using var recoveryContext = database.CreateContext();
        var recovered = await Manager(recoveryContext, emailService: email)
            .ResendVerificationAsync(new ResendVerificationDto { Email = "user1@example.test" });

        Assert.True(recovered.Success);
        Assert.Equal(Messages.VerificationResendGeneric, recovered.Message);
        Assert.Equal(2, email.VerificationAttempts.Count);
        Assert.NotEqual(failedCode, email.VerificationAttempts[1].Code);
        await using var verificationContext = database.CreateContext();
        var persisted = await verificationContext.EmailVerifications.SingleAsync(x => x.UserId == 1 && !x.IsVerified);
        Assert.Equal(email.VerificationAttempts[1].Code, persisted.Code);
        Assert.Null(persisted.DeliveryToken);
        Assert.False(persisted.DeliveryClaimed);
    }

    [Fact]
    public void DeliveryOwnershipUsesTheRevisionAndOpaqueTokenAsConcurrencyState()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var entity = context.Model.FindEntityType(typeof(EmailVerification))!;

        Assert.True(entity.FindProperty(nameof(EmailVerification.Version))!.IsConcurrencyToken);
        Assert.True(entity.FindProperty(nameof(EmailVerification.DeliveryToken))!.IsConcurrencyToken);
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

    private static UserForRegisterDto Registration(string email, string userName) => new()
    {
        Email = email,
        UserName = userName,
        FirstName = "Registration",
        LastName = "Test",
        Password = NewPassword
    };

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

    private static AuthManager Manager(
        TaskTrackerDbContext context,
        int currentUserId = 1,
        IEmailService? emailService = null) => new(
        new UnitOfWork(context),
        new Tokens(),
        emailService ?? new Email(),
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
        public bool FailVerificationDelivery { get; set; }
        public List<(string Email, string Code)> VerificationAttempts { get; } = [];

        public Task SendVerificationCodeAsync(string email, string code)
        {
            VerificationAttempts.Add((email, code));
            return FailVerificationDelivery
                ? Task.FromException(new IOException("Simulated verification email failure."))
                : Task.CompletedTask;
        }
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

    private sealed class BreakVerificationSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var verification = eventData.Context!.ChangeTracker.Entries<EmailVerification>().Single();
            verification.Property(x => x.Code).CurrentValue = null!;
            return ValueTask.FromResult(result);
        }
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
