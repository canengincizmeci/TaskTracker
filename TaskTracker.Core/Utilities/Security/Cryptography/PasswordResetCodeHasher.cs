using System.Security.Cryptography;
using System.Text;

namespace TaskTracker.Core.Utilities.Security.Cryptography
{
    public static class PasswordResetCodeHasher
    {
        public static string Hash(string normalizedEmail, string code, string secret)
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
                throw new InvalidOperationException("Password recovery HMAC secret must contain at least 32 characters.");

            var key = Encoding.UTF8.GetBytes(secret);
            var value = Encoding.UTF8.GetBytes($"password-reset:{normalizedEmail}:{code}");

            using var hmac = new HMACSHA256(key);
            return Convert.ToHexString(hmac.ComputeHash(value));
        }
    }
}
