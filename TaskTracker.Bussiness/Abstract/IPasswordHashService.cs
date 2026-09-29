using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Security.Hashing;

namespace TaskTracker.Bussiness.Abstract;

public enum PasswordVerificationOutcome
{
    Failed,
    Valid,
    ValidNeedsUpgrade
}

public sealed record PasswordHashData(
    byte[] Hash,
    byte[] Salt,
    PasswordHashVersion Version);

public interface IPasswordHashService
{
    PasswordHashData CreateHash(User user, string password);
    PasswordVerificationOutcome Verify(User user, string? password);
}
