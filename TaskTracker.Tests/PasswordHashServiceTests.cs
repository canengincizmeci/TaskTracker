using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Security.Hashing;

namespace TaskTracker.Tests;

public class PasswordHashServiceTests
{
    private const string Password = "Case Sensitive Password";

    [Fact]
    public void LegacyCorrectPasswordNeedsUpgrade()
    {
        HashingHelper.CreatePasswordHash(Password, out var hash, out var salt);
        var user = User(hash, salt, PasswordHashVersion.LegacyHmacSha512);

        Assert.Equal(PasswordVerificationOutcome.ValidNeedsUpgrade, Service().Verify(user, Password));
    }

    [Fact]
    public void LegacyWrongPasswordFails()
    {
        HashingHelper.CreatePasswordHash(Password, out var hash, out var salt);
        var user = User(hash, salt, PasswordHashVersion.LegacyHmacSha512);

        Assert.Equal(PasswordVerificationOutcome.Failed, Service().Verify(user, "wrong password"));
    }

    [Fact]
    public void NullLegacyHashFailsSafely()
    {
        var user = User(null!, new byte[128], PasswordHashVersion.LegacyHmacSha512);

        Assert.Equal(PasswordVerificationOutcome.Failed, Service().Verify(user, Password));
    }

    [Fact]
    public void EmptyLegacyHashFailsSafely()
    {
        var user = User([], new byte[128], PasswordHashVersion.LegacyHmacSha512);

        Assert.Equal(PasswordVerificationOutcome.Failed, Service().Verify(user, Password));
    }

    [Fact]
    public void ShortLegacyHashFailsSafely()
    {
        var user = User(new byte[63], new byte[128], PasswordHashVersion.LegacyHmacSha512);

        Assert.Equal(PasswordVerificationOutcome.Failed, Service().Verify(user, Password));
    }

    [Fact]
    public void OversizedLegacyHashFailsSafely()
    {
        HashingHelper.CreatePasswordHash(Password, out var hash, out var salt);
        var user = User([.. hash, 0], salt, PasswordHashVersion.LegacyHmacSha512);

        Assert.Equal(PasswordVerificationOutcome.Failed, Service().Verify(user, Password));
    }

    [Fact]
    public void NullLegacySaltFailsSafely()
    {
        var user = User(new byte[64], null!, PasswordHashVersion.LegacyHmacSha512);

        Assert.Equal(PasswordVerificationOutcome.Failed, Service().Verify(user, Password));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(129)]
    public void StructurallyInvalidLegacySaltFailsSafely(int saltLength)
    {
        var user = User(new byte[64], new byte[saltLength], PasswordHashVersion.LegacyHmacSha512);

        Assert.Equal(PasswordVerificationOutcome.Failed, Service().Verify(user, Password));
    }

    [Fact]
    public void NullLegacyPasswordFailsSafely()
    {
        HashingHelper.CreatePasswordHash(Password, out var hash, out var salt);
        var user = User(hash, salt, PasswordHashVersion.LegacyHmacSha512);

        Assert.Equal(PasswordVerificationOutcome.Failed, Service().Verify(user, null));
    }

    [Fact]
    public void IdentityCorrectAndWrongPasswordsAreDistinguished()
    {
        var service = Service();
        var user = User([], [], PasswordHashVersion.IdentityV3);
        Apply(user, service.CreateHash(user, Password));

        Assert.Equal(PasswordVerificationOutcome.Valid, service.Verify(user, Password));
        Assert.Equal(PasswordVerificationOutcome.Failed, service.Verify(user, "wrong password"));
    }

    [Fact]
    public void LowerWorkFactorNeedsUpgrade()
    {
        var user = User([], [], PasswordHashVersion.IdentityV3);
        Apply(user, Service(10_000).CreateHash(user, Password));

        Assert.Equal(PasswordVerificationOutcome.ValidNeedsUpgrade, Service(20_000).Verify(user, Password));
    }

    [Fact]
    public void MalformedIdentityPayloadFailsSafely()
    {
        var user = User([1, 2, 3], [], PasswordHashVersion.IdentityV3);

        Assert.Equal(PasswordVerificationOutcome.Failed, Service().Verify(user, Password));
    }

    [Fact]
    public void UnknownVersionFailsClosed()
    {
        var user = User([1], [2], (PasswordHashVersion)99);

        Assert.Equal(PasswordVerificationOutcome.Failed, Service().Verify(user, Password));
    }

    [Fact]
    public void SamePasswordProducesDifferentPayloads()
    {
        var service = Service();
        var first = service.CreateHash(User([], [], PasswordHashVersion.IdentityV3), Password);
        var second = service.CreateHash(User([], [], PasswordHashVersion.IdentityV3), Password);

        Assert.NotEqual(first.Hash, second.Hash);
        Assert.Empty(first.Salt);
        Assert.Empty(second.Salt);
        Assert.Equal(PasswordHashVersion.IdentityV3, first.Version);
    }

    internal static PasswordHashService Service(int iterations = 10_000) => new(
        Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = iterations
        }));

    internal static void Apply(User user, PasswordHashData data)
    {
        user.PasswordHash = data.Hash;
        user.PasswordSalt = data.Salt;
        user.PasswordHashVersion = data.Version;
    }

    internal static User User(byte[] hash, byte[] salt, PasswordHashVersion version) => new()
    {
        FirstName = "Password",
        LastName = "Tester",
        UserName = Guid.NewGuid().ToString("N"),
        NormalizedUserName = Guid.NewGuid().ToString("N"),
        Email = $"{Guid.NewGuid():N}@example.test",
        PasswordHash = hash,
        PasswordSalt = salt,
        PasswordHashVersion = version,
        Status = true,
        IsVerified = true
    };
}
