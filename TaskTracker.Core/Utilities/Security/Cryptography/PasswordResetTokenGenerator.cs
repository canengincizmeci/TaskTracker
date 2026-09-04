using System.Security.Cryptography;
using System.Text;

namespace TaskTracker.Core.Utilities.Security.Cryptography
{
    public static class PasswordResetTokenGenerator
    {
        private const int TokenSizeInBytes = 32;

        public static string GenerateToken()
        {
            var tokenBytes = RandomNumberGenerator.GetBytes(TokenSizeInBytes);

            return Convert.ToBase64String(tokenBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static string HashToken(string resetToken)
        {
            if (string.IsNullOrWhiteSpace(resetToken))
                throw new ArgumentException("Reset token cannot be empty.", nameof(resetToken));

            var tokenBytes = Encoding.UTF8.GetBytes(resetToken);
            return Convert.ToHexString(SHA256.HashData(tokenBytes));
        }
    }
}
