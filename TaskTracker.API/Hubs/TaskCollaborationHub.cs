using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TaskTracker.Bussiness.Abstract;
using System.Collections.Concurrent;

namespace TaskTracker.API.Hubs
{
    [Authorize]
    public class TaskCollaborationHub : Hub
    {
        private static readonly ConcurrentDictionary<(int TaskId, int UserId, string ConnectionId), byte> Memberships = new();
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
            var connectionId = Context.ConnectionId;
            await Groups.AddToGroupAsync(connectionId, TaskGroup(taskId));
            Memberships.TryAdd((taskId, userId, connectionId), 0);
            if (!await _collaboration.CanAccessAsync(taskId, userId))
            {
                await LeaveTask(taskId);
                throw new HubException("You do not have access to this task workspace.");
            }
        }

        public async Task LeaveTask(int taskId)
        {
            if (int.TryParse(Context.UserIdentifier, out var userId))
                Forget(taskId, userId, Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskGroup(taskId));
        }

        internal static IReadOnlyCollection<string> ConnectionsFor(int taskId, int userId) =>
            Memberships.Keys.Where(entry => entry.TaskId == taskId && entry.UserId == userId)
                .Select(entry => entry.ConnectionId).ToArray();

        internal static void Forget(int taskId, int userId, string connectionId) =>
            Memberships.TryRemove((taskId, userId, connectionId), out _);

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            foreach (var entry in Memberships.Keys.Where(entry => entry.ConnectionId == Context.ConnectionId))
                Memberships.TryRemove(entry, out _);
            return base.OnDisconnectedAsync(exception);
        }
    }
}
