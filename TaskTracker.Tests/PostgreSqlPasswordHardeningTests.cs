using Microsoft.EntityFrameworkCore;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Security.Hashing;

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
}
