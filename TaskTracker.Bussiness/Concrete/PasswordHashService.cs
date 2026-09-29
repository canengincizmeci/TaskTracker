using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Security.Hashing;

namespace TaskTracker.Bussiness.Concrete;

public sealed class PasswordHashService : IPasswordHashService
{
    private const int LegacyHashLength = 64;
    private const int LegacySaltLength = 128;
    private readonly PasswordHasher<User> identityHasher;

    public PasswordHashService(IOptions<PasswordHasherOptions> options)
    {
        identityHasher = new PasswordHasher<User>(options);
    }

    public PasswordHashData CreateHash(User user, string password)
    {
        var encodedHash = identityHasher.HashPassword(user, password);
        return new PasswordHashData(
            Convert.FromBase64String(encodedHash),
            Array.Empty<byte>(),
            PasswordHashVersion.IdentityV3);
    }

    public PasswordVerificationOutcome Verify(User user, string? password)
    {
        if (user.PasswordHashVersion == PasswordHashVersion.LegacyHmacSha512)
        {
            if (password is null ||
                user.PasswordHash is null || user.PasswordHash.Length != LegacyHashLength ||
                user.PasswordSalt is null || user.PasswordSalt.Length != LegacySaltLength)
            {
                return PasswordVerificationOutcome.Failed;
            }

            try
            {
                return HashingHelper.VerifyPasswordHash(password, user.PasswordHash, user.PasswordSalt)
                    ? PasswordVerificationOutcome.ValidNeedsUpgrade
                    : PasswordVerificationOutcome.Failed;
            }
            catch (Exception exception) when (exception is ArgumentException or CryptographicException)
            {
                return PasswordVerificationOutcome.Failed;
            }
        }

        if (user.PasswordHashVersion != PasswordHashVersion.IdentityV3)
            return PasswordVerificationOutcome.Failed;

        if (password is null || user.PasswordHash is null)
            return PasswordVerificationOutcome.Failed;

        try
        {
            var encodedHash = Convert.ToBase64String(user.PasswordHash);
            return identityHasher.VerifyHashedPassword(user, encodedHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordVerificationOutcome.Valid,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.ValidNeedsUpgrade,
                _ => PasswordVerificationOutcome.Failed
            };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException)
        {
            return PasswordVerificationOutcome.Failed;
        }
    }
}
