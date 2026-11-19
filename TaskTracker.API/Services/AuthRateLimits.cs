using System.Globalization;
using System.Threading.RateLimiting;

namespace TaskTracker.API.Services;

public static class AuthRateLimits
{
    public const string Verify = "verification-attempts";
    public const string Resend = "verification-resend";

    public static IServiceCollection AddAuthRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "Too many requests. Wait a minute and try again." }, cancellationToken);
            };
            options.AddPolicy(Verify, context => Window(context, 30));
            options.AddPolicy(Resend, context => Window(context, 6));
        });

    private static RateLimitPartition<string> Window(HttpContext context, int permits) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6
            });
}
