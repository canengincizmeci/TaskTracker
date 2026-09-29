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
}
