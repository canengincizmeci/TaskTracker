using Microsoft.AspNetCore.SignalR;
using TaskTracker.API.Hubs;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.API.Services
{
    public class SignalRTaskWorkspaceService : ITaskWorkspaceRealtimeService
    {
        private readonly IHubContext<TaskWorkspaceHub> _hub;

        public SignalRTaskWorkspaceService(IHubContext<TaskWorkspaceHub> hub)
        {
            _hub = hub;
        }

        public Task ActivityCreatedAsync(int taskId, TaskActivityDto activity) =>
            _hub.Clients.Group(TaskWorkspaceHub.TaskGroup(taskId)).SendAsync("TaskActivityCreated", activity);

        public Task MessageCreatedAsync(int taskId, TaskMessageDto message) =>
            _hub.Clients.Group(TaskWorkspaceHub.TaskGroup(taskId)).SendAsync("TaskMessageCreated", message);

        public Task TaskChangedAsync(int taskId) =>
            _hub.Clients.Group(TaskWorkspaceHub.TaskGroup(taskId)).SendAsync("TaskChanged", taskId);

        public async Task AccessRevokedAsync(int taskId, int userId)
        {
            foreach (var connectionId in TaskWorkspaceHub.ConnectionsFor(taskId, userId))
            {
                await _hub.Clients.Client(connectionId).SendAsync("TaskAccessRevoked", taskId);
                await _hub.Groups.RemoveFromGroupAsync(connectionId, TaskWorkspaceHub.TaskGroup(taskId));
                TaskWorkspaceHub.Forget(taskId, userId, connectionId);
            }
        }
    }
}
