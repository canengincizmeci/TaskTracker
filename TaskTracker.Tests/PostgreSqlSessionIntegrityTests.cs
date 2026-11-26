using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using TaskTracker.API.Controllers;
using TaskTracker.API.Services;
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

public class PostgreSqlSessionIntegrityTests
{
    private const string OriginalPassword = "Before session update 80!";
    private const string NewPassword = "After session update 42!";
    private static readonly string SigningKey = new('s', 64);
    private const string Audience = "session-test";
    private const string Issuer = "session-test-issuer";

    [PostgreSqlFact]
    public async Task Concurrent_refresh_consumes_a_token_once()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (original, _) = await StartSessionsAsync(database);
        var beforeInsert = new PostgreSqlCommandGate("INSERT INTO \"RefreshTokens\"", pause: true);
        var beforeLock = new PostgreSqlCommandGate("FOR UPDATE", pause: false);
        await using var first = database.CreateContext(beforeInsert);
        await using var second = database.CreateContext(beforeLock);

        var rotating = Rotate(first, original.RefreshToken);
        Task<IDataResult<TokenResponseDto>> competing;
        try
        {
            await beforeInsert.Arrived;
            competing = Rotate(second, original.RefreshToken);
            await beforeLock.Arrived;
        }
        finally
        {
            beforeInsert.Release();
        }

        var winner = await rotating;
        Assert.True(winner.Success);
        Assert.False((await competing).Success);
        await using var verify = database.CreateContext();
        var originalDigest = RefreshTokenCredential.DigestPresentedToken(original.RefreshToken);
        var originalToken = await verify.RefreshTokens.SingleAsync(token => token.TokenDigest == originalDigest);
        var active = await verify.RefreshTokens.SingleAsync(token => token.SessionId == originalToken.SessionId && !token.IsRevoked);
        Assert.Equal(RefreshTokenCredential.DigestPresentedToken(winner.Data.RefreshToken), active.TokenDigest);
        Assert.True(await IsActiveAsync(verify, original.AccessToken.Token));
        Assert.True(await IsActiveAsync(verify, winner.Data.AccessToken));
    }

    [PostgreSqlFact]
    public async Task Stored_digest_cannot_rotate_or_revoke_the_session()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (original, _) = await StartSessionsAsync(database);
        await using var context = database.CreateContext();
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(original.RefreshToken)));
        var record = await context.RefreshTokens.SingleAsync(token => token.TokenDigest == expected);
        Assert.Equal(32, Base64UrlEncoder.DecodeBytes(original.RefreshToken).Length);
        Assert.NotEqual(original.RefreshToken, record.TokenDigest);
        Assert.False((await Rotate(context, record.TokenDigest)).Success);
        await Manager(context).LogoutAsync(new() { RefreshToken = record.TokenDigest });
        Assert.True(await IsActiveAsync(context, original.AccessToken.Token));
        Assert.True((await Rotate(context, original.RefreshToken)).Success);
    }

    [PostgreSqlTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Password_update_invalidates_all_sessions_regardless_of_refresh_order(bool reset, bool refreshFirst)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (original, other) = await StartSessionsAsync(database);
        const string resetToken = "test-reset-session";
        if (reset)
        {
            await using var seed = database.CreateContext();
            seed.PasswordResetRequests.Add(new PasswordResetRequest
            {
                UserId = 1, CodeHash = "test-code-hash", ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                VerifiedAt = DateTime.UtcNow, ResetTokenHash = PasswordResetTokenGenerator.HashToken(resetToken),
                ResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(10)
            });
            await seed.SaveChangesAsync();
        }

        Task<IResult> UpdatePassword(TaskTrackerDbContext context) => reset
            ? Manager(context).ResetPasswordAsync(new()
                { ResetToken = resetToken, NewPassword = NewPassword, ConfirmNewPassword = NewPassword })
            : Manager(context).ChangePasswordAsync(new()
                { CurrentPassword = OriginalPassword, NewPassword = NewPassword, ConfirmNewPassword = NewPassword });

        var beforeWrite = new PostgreSqlCommandGate(
            refreshFirst ? "INSERT INTO \"RefreshTokens\"" : "UPDATE \"Users\"", pause: true);
        var beforeLock = new PostgreSqlCommandGate("FOR UPDATE", pause: false);
        await using var first = database.CreateContext(beforeWrite);
        await using var second = database.CreateContext(beforeLock);
        Task<IDataResult<TokenResponseDto>> rotating;
        Task<IResult> updating;
        try
        {
            if (refreshFirst)
            {
                rotating = Rotate(first, original.RefreshToken);
                await beforeWrite.Arrived;
                updating = UpdatePassword(second);
            }
            else
            {
                updating = UpdatePassword(first);
                await beforeWrite.Arrived;
                rotating = Rotate(second, original.RefreshToken);
            }
            await beforeLock.Arrived;
        }
        finally
        {
            beforeWrite.Release();
        }

        Assert.True((await updating).Success);
        var rotation = await rotating;
        Assert.Equal(refreshFirst, rotation.Success);
        await using var verify = database.CreateContext();
        Assert.Equal(1, (await verify.Users.SingleAsync(user => user.Id == 1)).SessionVersion);
        Assert.Empty(await verify.RefreshTokens.Where(token => token.UserId == 1 && !token.IsRevoked).ToListAsync());
        Assert.False(await IsActiveAsync(verify, original.AccessToken.Token));
        Assert.False(await IsActiveAsync(verify, other.AccessToken.Token));
        Assert.False((await Rotate(verify, other.RefreshToken)).Success);
        if (rotation.Success)
        {
            Assert.False(await IsActiveAsync(verify, rotation.Data.AccessToken));
            Assert.False((await Rotate(verify, rotation.Data.RefreshToken)).Success);
        }
        var newLogin = await LoginAsync(verify, NewPassword);
        Assert.True(await IsActiveAsync(verify, newLogin.AccessToken.Token));
    }

    [PostgreSqlFact]
    public async Task Logout_revokes_the_rotated_session_and_preserves_other_sessions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (original, other) = await StartSessionsAsync(database);
        var beforeInsert = new PostgreSqlCommandGate("INSERT INTO \"RefreshTokens\"", pause: true);
        var beforeLock = new PostgreSqlCommandGate("FOR UPDATE", pause: false);
        await using var first = database.CreateContext(beforeInsert);
        await using var second = database.CreateContext(beforeLock);
        var controller = new AuthController(Manager(second));
        var dto = new RefreshTokenDto { RefreshToken = original.RefreshToken };
        var rotating = Rotate(first, original.RefreshToken);
        Task<IActionResult> signingOut;
        try
        {
            await beforeInsert.Arrived;
            signingOut = controller.Logout(dto);
            await beforeLock.Arrived;
        }
        finally
        {
            beforeInsert.Release();
        }
        var rotation = await rotating;
        Assert.True(rotation.Success);
        Assert.IsType<NoContentResult>(await signingOut);
        Assert.IsType<NoContentResult>(await controller.Logout(dto));

        await using var verify = database.CreateContext();
        Assert.False(await IsActiveAsync(verify, original.AccessToken.Token));
        Assert.False(await IsActiveAsync(verify, rotation.Data.AccessToken));
        Assert.False((await Rotate(verify, rotation.Data.RefreshToken)).Success);
        Assert.True(await IsActiveAsync(verify, other.AccessToken.Token));
        Assert.True((await Rotate(verify, other.RefreshToken)).Success);
    }

    [PostgreSqlTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Disabled_or_unverified_accounts_cannot_refresh_or_authenticate(bool enabled, bool verified)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var (original, _) = await StartSessionsAsync(database);
        await using (var update = database.CreateContext())
        {
            var user = await update.Users.SingleAsync(user => user.Id == 1);
            user.Status = enabled;
            user.IsVerified = verified;
            await update.SaveChangesAsync();
        }

        await using var verify = database.CreateContext();
        Assert.False(await IsActiveAsync(verify, original.AccessToken.Token));
        Assert.False((await Rotate(verify, original.RefreshToken)).Success);
    }

    [PostgreSqlFact]
    public async Task Migrations_remove_legacy_credentials_and_initialize_session_version()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("20261122160631_AddSubTaskSubmissionDeliveries");
        await using var context = database.CreateContext();
        const string legacy = "legacy-migration-token";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "RefreshTokens" ("Token", "UserId", "CreatedAt", "Expires", "IsRevoked")
            VALUES ({legacy}, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP + INTERVAL '1 day', FALSE)
            """);

        await context.Database.MigrateAsync();
        Assert.Empty(await context.RefreshTokens.ToListAsync());
        Assert.Equal(0, (await context.Users.SingleAsync(user => user.Id == 1)).SessionVersion);
        Assert.False((await Rotate(context, legacy)).Success);
    }

    [PostgreSqlFact]
    public async Task Hash_migration_invalidates_an_active_batch03_session()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("20261124005759_AddAttachmentRetryDigest");
        await using var context = database.CreateContext();
        var user = await context.Users.SingleAsync(user => user.Id == 1);
        user.IsVerified = true;
        await context.SaveChangesAsync();
        var sessionId = Guid.NewGuid();
        var oldAccessToken = new JwtHelper(TokenConfiguration()).CreateToken(user, [], sessionId).Token;
        var oldRefreshToken = Guid.NewGuid().ToString();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "RefreshTokens" ("Token", "SessionId", "UserId", "CreatedAt", "Expires", "IsRevoked")
            VALUES ({oldRefreshToken}, {sessionId}, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP + INTERVAL '1 day', FALSE)
            """);
        Assert.True(await IsActiveAsync(context, oldAccessToken));

        await context.Database.MigrateAsync();
        Assert.False(await IsActiveAsync(context, oldAccessToken));
        Assert.False((await Rotate(context, oldRefreshToken)).Success);
        Assert.Empty(await context.RefreshTokens.ToListAsync());
    }

    private static async Task<(LoginResponseDto Original, LoginResponseDto Other)> StartSessionsAsync(PostgreSqlTestDatabase database)
    {
        await using var context = database.CreateContext();
        var user = await context.Users.SingleAsync(user => user.Id == 1);
        var password = PasswordHashServiceTests.Service().CreateHash(user, OriginalPassword);
        user.PasswordHash = password.Hash;
        user.PasswordSalt = password.Salt;
        user.PasswordHashVersion = password.Version;
        user.IsVerified = true;
        await context.SaveChangesAsync();
        return (await LoginAsync(context, OriginalPassword), await LoginAsync(context, OriginalPassword));
    }

    private static async Task<LoginResponseDto> LoginAsync(TaskTrackerDbContext context, string password)
    {
        var result = await Manager(context).LoginAsync(new() { Email = "pg-user1@example.test", Password = password });
        Assert.True(result.Success);
        return result.Data;
    }

    private static Task<IDataResult<TokenResponseDto>> Rotate(TaskTrackerDbContext context, string token) =>
        Manager(context).RefreshTokenAsync(new() { RefreshToken = token });

    private static Task<bool> IsActiveAsync(TaskTrackerDbContext context, string token)
    {
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidIssuer = Issuer, ValidAudience = Audience, ValidateIssuerSigningKey = true, ClockSkew = TimeSpan.Zero,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey))
        }, out _);
        return SessionValidation.IsActiveAsync(context, principal, CancellationToken.None);
    }

    private static AuthManager Manager(TaskTrackerDbContext context)
    {
        var config = TokenConfiguration();
        return new(new UnitOfWork(context), new JwtHelper(config), new NoEmail(), new CurrentUser(), config,
            NullLogger<AuthManager>.Instance, PasswordHashServiceTests.Service(), new IdentityNormalizer());
    }

    private static IConfiguration TokenConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TokenOptions:SecurityKey"] = SigningKey, ["TokenOptions:Issuer"] = Issuer, ["TokenOptions:Audience"] = Audience,
            ["TokenOptions:AccessTokenExpiration"] = "30", ["TokenOptions:RefreshTokenExpirationDays"] = "7"
        }).Build();

    private sealed class CurrentUser : ICurrentUserService
    {
        public int UserId => 1;
    }

    private sealed class NoEmail : IEmailService
    {
        public Task SendVerificationCodeAsync(string email, string code) => Task.CompletedTask;
        public Task SendPasswordResetCodeAsync(string email, string code) => Task.CompletedTask;
        public Task SendTaskShareInvitationEmailAsync(string email, string taskTitle, string inviterUsername, string invitationUrl) =>
            Task.CompletedTask;
    }
}
