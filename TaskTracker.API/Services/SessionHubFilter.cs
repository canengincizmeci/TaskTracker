using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.DataAccess;

namespace TaskTracker.API.Services;

internal sealed class SessionHubFilter(IServiceScopeFactory scopes, ILogger<SessionHubFilter> logger)
    : BackgroundService, IHubFilter
{
    private sealed record Connection(HubCallerContext Context, SessionIdentity Session);

    private readonly ConcurrentDictionary<string, Connection> connections = new();

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (!SessionValidation.TryRead(context.Context.User, out var session))
        {
            context.Context.Abort();
            return;
        }

        connections[context.Context.ConnectionId] = new(context.Context, session);
        try
        {
            if (await IsActiveAsync(context.Context, session))
            {
                await next(context);
                return;
            }
        }
        catch
        {
            connections.TryRemove(context.Context.ConnectionId, out _);
            throw;
        }

        connections.TryRemove(context.Context.ConnectionId, out _);
        context.Context.Abort();
    }

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        connections.TryRemove(context.Context.ConnectionId, out _);
        return next(context, exception);
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (connections.TryGetValue(context.Context.ConnectionId, out var connection) &&
            await IsActiveAsync(context.Context, connection.Session))
            return await next(context);

        context.Context.Abort();
        throw new HubException("The session is no longer active.");
    }

    private async Task<bool> IsActiveAsync(HubCallerContext context, SessionIdentity session)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await SessionValidation.IsActiveAsync(scope.ServiceProvider.GetRequiredService<TaskTrackerDbContext>(),
            session, context.ConnectionAborted);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Idle sockets can still receive broadcasts without submitting another JWT or invoking a method.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var batch in connections.Values.Chunk(256))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<TaskTrackerDbContext>();
                    var ids = batch.Select(connection => connection.Session.SessionId).Distinct().ToArray();
                    var active = await SessionValidation.ActiveTokens(db).Where(token => ids.Contains(token.SessionId))
                        .Select(token => new SessionIdentity(token.UserId, token.SessionId, token.User.SessionVersion))
                        .ToListAsync(stoppingToken);
                    var activeSessions = active.ToHashSet();
                    foreach (var connection in batch)
                        if (!activeSessions.Contains(connection.Session))
                            connection.Context.Abort();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not verify active SignalR sessions.");
                    foreach (var connection in batch)
                        connection.Context.Abort();
                }
            }
        }
    }
}
