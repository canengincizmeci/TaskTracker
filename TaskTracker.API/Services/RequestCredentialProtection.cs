using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace TaskTracker.API.Services;

public static class RequestCredentialProtection
{
    private const string QueryToken = "access_token";
    private static readonly object HubTokenKey = new();

    public static void ConfigureLogging(ILoggingBuilder logging)
    {
        // Hosting emits the raw query before application middleware runs.
        logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Warning);
        logging.AddFilter("Microsoft.AspNetCore.Authentication.JwtBearer", LogLevel.Warning);
        logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);
        logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Warning);
        logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Warning);
    }

    public static async Task RemoveQueryTokenAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        if (request.QueryString.HasValue && request.Query.TryGetValue(QueryToken, out var token))
        {
            if (token.Count == 1 && !StringValues.IsNullOrEmpty(token) &&
                (request.Path.Equals("/hubs/notifications") || request.Path.Equals("/hubs/task-workspace")))
                context.Items[HubTokenKey] = token[0]!;

            request.Query = new QueryCollection(request.Query
                .Where(pair => !string.Equals(pair.Key, QueryToken, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));

            if (context.Features.Get<IHttpRequestFeature>() is { } feature)
            {
                var separator = feature.RawTarget.IndexOf('?');
                if (separator >= 0)
                    feature.RawTarget = feature.RawTarget[..separator] + request.QueryString.Value;
            }

            if (Activity.Current is { } activity)
            {
                activity.SetTag("url.query", null);
                foreach (var key in new[] { "url.full", "http.url", "http.target" })
                {
                    if (activity.GetTagItem(key) is string value)
                        activity.SetTag(key, value.Split('?', 2)[0]);
                }
            }
        }

        await next(context);
    }

    public static Task ReadHubTokenAsync(MessageReceivedContext context)
    {
        if (context.HttpContext.Items.Remove(HubTokenKey, out var token) &&
            !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            context.Token = token as string;

        return Task.CompletedTask;
    }
}
