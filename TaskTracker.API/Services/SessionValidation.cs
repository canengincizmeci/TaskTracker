using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Security.Jwt;

namespace TaskTracker.API.Services;

internal readonly record struct SessionIdentity(int UserId, Guid SessionId, long Version);

public static class SessionValidation
{
    public static Task<bool> IsActiveAsync(TaskTrackerDbContext db, ClaimsPrincipal? principal, CancellationToken ct) =>
        TryRead(principal, out var session) ? IsActiveAsync(db, session, ct) : Task.FromResult(false);

    internal static bool TryRead(ClaimsPrincipal? principal, out SessionIdentity session)
    {
        session = default;
        if (principal is null ||
            !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0 ||
            !Guid.TryParse(principal.FindFirstValue(JwtHelper.SessionIdClaim), out var sessionId) || sessionId == Guid.Empty ||
            !long.TryParse(principal.FindFirstValue(JwtHelper.SessionVersionClaim), NumberStyles.None,
                CultureInfo.InvariantCulture, out var version))
            return false;

        session = new(userId, sessionId, version);
        return true;
    }

    internal static IQueryable<RefreshToken> ActiveTokens(TaskTrackerDbContext db)
    {
        var now = DateTime.UtcNow;
        return db.RefreshTokens.AsNoTracking().Where(token =>
            !token.IsRevoked && token.Expires > now && token.User.Status && token.User.IsVerified);
    }

    internal static Task<bool> IsActiveAsync(TaskTrackerDbContext db, SessionIdentity session, CancellationToken ct) =>
        ActiveTokens(db).AnyAsync(token => token.UserId == session.UserId &&
            token.SessionId == session.SessionId && token.User.SessionVersion == session.Version, ct);
}
