namespace TaskTracker.Core.Utilities.Security.Hashing;

public enum PasswordHashVersion : short
{
    LegacyHmacSha512 = 1,
    IdentityV3 = 2
}
