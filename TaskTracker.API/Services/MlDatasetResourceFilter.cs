using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Threading.RateLimiting;

namespace TaskTracker.API.Services;

public sealed class MlDatasetResourceFilter : IAsyncResourceFilter, IDisposable
{
    public const int MaxConcurrentRequests = 2;

    private readonly ConcurrencyLimiter limiter = new(new ConcurrencyLimiterOptions
    {
        PermitLimit = MaxConcurrentRequests,
        QueueLimit = 0
    });

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "no-store";
        using var lease = limiter.AttemptAcquire();
        if (!lease.IsAcquired)
        {
            response.Headers.RetryAfter = "1";
            context.Result = new ObjectResult(new { message = "Dataset requests are at capacity. Try again shortly." })
                { StatusCode = StatusCodes.Status429TooManyRequests };
            return;
        }

        await next();
    }

    public void Dispose() => limiter.Dispose();
}
