using System.Globalization;
using System.Threading.RateLimiting;

namespace TaskTracker.API.Services;

public enum AuthOperation
{
    Login, Register, Refresh, ForgotPassword, VerifyResetCode, ResetPassword
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class AuthRateLimitAttribute(AuthOperation operation) : Attribute
{
    public AuthOperation Operation { get; } = operation;
}

public static class AuthRateLimits
{
    public const string Verify = "verification-attempts";
    public const string Resend = "verification-resend";
    public const int MaxRequestBytes = 8 * 1024;
    internal const string RejectionMessage = "Too many requests. Wait and try again.";

    public static IServiceCollection AddAuthRateLimits(this IServiceCollection services)
    {
        services.AddSingleton<AuthIdentityRateLimiter>();
        services.AddScoped<AuthIdentityRateLimitFilter>();
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                SetRetryAfter(context.HttpContext.Response, context.Lease);
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = RejectionMessage }, cancellationToken);
            };
            options.AddPolicy(Verify, context => Window(ClientIp(context), 30));
            options.AddPolicy(Resend, context => Window(ClientIp(context), 6));
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, (AuthOperation?, string)>(context =>
                    GetOperation(context) is { } operation
                        ? Window<(AuthOperation?, string)>((operation, ClientIp(context)), Limits(operation).Ip)
                        : RateLimitPartition.GetNoLimiter<(AuthOperation?, string)>((null, ""))),
                PartitionedRateLimiter.Create<HttpContext, bool>(context =>
                    GetOperation(context) is not null
                        ? RateLimitPartition.GetConcurrencyLimiter(true, _ => new ConcurrencyLimiterOptions
                            { PermitLimit = 24, QueueLimit = 0 })
                        : RateLimitPartition.GetNoLimiter(false)),
                PartitionedRateLimiter.Create<HttpContext, bool>(context =>
                    GetOperation(context) is not null
                        ? Window(true, 300)
                        : RateLimitPartition.GetNoLimiter(false)));
        });
    }

    internal static AuthOperation? GetOperation(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<AuthRateLimitAttribute>()?.Operation;

    internal static (int Ip, int Identity) Limits(AuthOperation operation) => operation switch
    {
        AuthOperation.Login => (20, 10),
        AuthOperation.Register => (8, 3),
        AuthOperation.Refresh => (60, 20),
        AuthOperation.ForgotPassword => (8, 3),
        AuthOperation.VerifyResetCode => (24, 10),
        AuthOperation.ResetPassword => (12, 6),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    internal static void SetRetryAfter(HttpResponse response, RateLimitLease lease)
    {
        var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? retry.TotalSeconds : 1;
        response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(seconds)).ToString(CultureInfo.InvariantCulture);
    }

    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown";

    internal static RateLimitPartition<TKey> Window<TKey>(TKey key, int permits) =>
        RateLimitPartition.GetSlidingWindowLimiter(
            key,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            });
}
