using System.Security.Cryptography;
using System.Text;

namespace TaskTracker.Core.Utilities.Security.Cryptography
{
    public static class PasswordResetCodeHasher
    {
        public static string Hash(string normalizedEmail, string code, string secret)
        {
            return Convert.ToHexString(ComputeHash(normalizedEmail, code, secret));
        }

        public static bool Verify(string normalizedEmail, string code, string secret, string? storedHash)
        {
            if (string.IsNullOrWhiteSpace(storedHash))
                return false;

            byte[] storedHashBytes;

            try
            {
                storedHashBytes = Convert.FromHexString(storedHash);
            }
            catch (FormatException)
            {
                return false;
            }

            if (storedHashBytes.Length != SHA256.HashSizeInBytes)
                return false;

            var computedHashBytes = ComputeHash(normalizedEmail, code, secret);
            return CryptographicOperations.FixedTimeEquals(computedHashBytes, storedHashBytes);
        }

        private static byte[] ComputeHash(string normalizedEmail, string code, string secret)
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
                throw new InvalidOperationException("Password recovery HMAC secret must contain at least 32 characters.");

            var key = Encoding.UTF8.GetBytes(secret);
            var value = Encoding.UTF8.GetBytes($"password-reset:{normalizedEmail}:{code}");

            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(value);
        }
    }
}
