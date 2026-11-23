using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace TaskTracker.API.Services;

public static class TrustedProxyHeaders
{
    public static void AddTrustedProxyHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = (configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
            .Select(ParseAddress).Distinct().ToArray();

        // Override the hosting defaults, including the permissive hosting environment flag.
        services.PostConfigure<ForwardedHeadersOptions>(options =>
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.ForwardLimit = 1;
            options.ForwardedHeaders = proxies.Length == 0
                ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in proxies)
                options.KnownProxies.Add(proxy);
        });
    }

    private static IPAddress ParseAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var address) ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            throw new InvalidOperationException("ReverseProxy:KnownProxies must contain exact proxy IP addresses.");

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
