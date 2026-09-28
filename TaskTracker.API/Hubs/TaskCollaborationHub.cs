using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TaskTracker.Bussiness.Abstract;
using System.Collections.Concurrent;

namespace TaskTracker.API.Hubs
{
    [Authorize]
    public class TaskCollaborationHub : Hub
    {
        private static readonly ConcurrentDictionary<(int TaskId, int UserId), ConcurrentDictionary<string, byte>> Memberships = new();
        private readonly ITaskCollaborationService _collaboration;

        public TaskCollaborationHub(ITaskCollaborationService collaboration)
        {
            _collaboration = collaboration;
        }

        public static string TaskGroup(int taskId) => $"task:{taskId}";

        public async Task JoinTask(int taskId)
        {
            if (!int.TryParse(Context.UserIdentifier, out var userId) ||
                !await _collaboration.CanAccessAsync(taskId, userId))
                throw new HubException("You do not have access to this task workspace.");
            await Groups.AddToGroupAsync(Context.ConnectionId, TaskGroup(taskId));
            Memberships.GetOrAdd((taskId, userId), _ => new()).TryAdd(Context.ConnectionId, 0);
        }

        public async Task LeaveTask(int taskId)
        {
            if (int.TryParse(Context.UserIdentifier, out var userId))
                Forget(taskId, userId, Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskGroup(taskId));
        }

        internal static IReadOnlyCollection<string> ConnectionsFor(int taskId, int userId) =>
            Memberships.TryGetValue((taskId, userId), out var connections) ? connections.Keys.ToArray() : [];

        internal static void Forget(int taskId, int userId, string connectionId)
        {
            if (!Memberships.TryGetValue((taskId, userId), out var connections)) return;
            connections.TryRemove(connectionId, out _);
            if (connections.IsEmpty) Memberships.TryRemove((taskId, userId), out _);
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            foreach (var entry in Memberships)
            {
                entry.Value.TryRemove(Context.ConnectionId, out _);
                if (entry.Value.IsEmpty) Memberships.TryRemove(entry.Key, out _);
            }
            return base.OnDisconnectedAsync(exception);
        }
    }
}
