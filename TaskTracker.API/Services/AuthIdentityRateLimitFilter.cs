using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.API.Services;

public sealed class AuthIdentityRateLimitFilter(IIdentityNormalizer normalizer, AuthIdentityRateLimiter limiter)
    : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (AuthRateLimits.GetOperation(context.HttpContext) is { } operation)
        {
            foreach (var argument in context.ActionArguments.Values)
            {
                var identity = argument switch
                {
                    UserForLoginDto login => normalizer.NormalizeEmail(login.Email),
                    UserForRegisterDto registration => normalizer.NormalizeEmail(registration.Email),
                    ForgotPasswordDto forgot => normalizer.NormalizeEmail(forgot.Email),
                    VerifyPasswordResetCodeDto verification => normalizer.NormalizeEmail(verification.Email),
                    RefreshTokenDto refresh => refresh.RefreshToken,
                    ResetPasswordDto reset => reset.ResetToken,
                    _ => null
                };
                if (identity is not null && !Admit(context, operation, "credential", identity))
                    return;
                if (argument is UserForRegisterDto registrationDto &&
                    !Admit(context, operation, "username", normalizer.NormalizeUserName(registrationDto.UserName)))
                    return;
            }
        }
        await next();
    }

    private bool Admit(ActionExecutingContext context, AuthOperation operation, string kind, string identity)
    {
        using var lease = limiter.Acquire(operation, kind, identity);
        if (lease.IsAcquired)
            return true;

        AuthRateLimits.SetRetryAfter(context.HttpContext.Response, lease);
        context.Result = new ObjectResult(new { message = AuthRateLimits.RejectionMessage })
            { StatusCode = StatusCodes.Status429TooManyRequests };
        return false;
    }
}

public sealed class AuthIdentityRateLimiter : IDisposable
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private readonly PartitionedRateLimiter<(AuthOperation Operation, string Kind, string Digest)> limiter =
        PartitionedRateLimiter.Create<(AuthOperation Operation, string Kind, string Digest), (AuthOperation, string, string)>(
            request => AuthRateLimits.Window(request, AuthRateLimits.Limits(request.Operation).Identity));

    internal RateLimitLease Acquire(AuthOperation operation, string kind, string identity)
    {
        var digest = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(identity)));
        return limiter.AttemptAcquire((operation, kind, digest));
    }

    public void Dispose() => limiter.Dispose();
}
