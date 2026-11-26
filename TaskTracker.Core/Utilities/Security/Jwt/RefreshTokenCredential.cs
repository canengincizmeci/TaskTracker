using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Utilities.Security.Jwt;

public static class RefreshTokenCredential
{
    public static (string Credential, RefreshToken Record) Issue(int userId, Guid sessionId, TimeSpan lifetime)
    {
        var credential = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        return (credential, new RefreshToken
        {
            UserId = userId,
            SessionId = sessionId,
            TokenDigest = Digest(credential),
            CreatedAt = now,
            Expires = now + lifetime
        });
    }

    public static string? DigestPresentedToken(string? credential) =>
        credential is { Length: 43 } && credential.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? Digest(credential)
            : null;

    private static string Digest(string credential) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(credential)));
}
