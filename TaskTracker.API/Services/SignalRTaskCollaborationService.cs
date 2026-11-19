using Microsoft.AspNetCore.SignalR;
using TaskTracker.API.Hubs;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.API.Services
{
    public class SignalRTaskCollaborationService : ITaskCollaborationRealtimeService
    {
        private readonly IHubContext<TaskCollaborationHub> _hub;

        public SignalRTaskCollaborationService(IHubContext<TaskCollaborationHub> hub)
        {
            _hub = hub;
        }

        public Task ActivityCreatedAsync(int taskId, TaskActivityDto activity) =>
            _hub.Clients.Group(TaskCollaborationHub.TaskGroup(taskId)).SendAsync("TaskActivityCreated", activity);

        public Task MessageCreatedAsync(int taskId, TaskMessageDto message) =>
            _hub.Clients.Group(TaskCollaborationHub.TaskGroup(taskId)).SendAsync("TaskMessageCreated", message);

        public Task TaskChangedAsync(int taskId) =>
            _hub.Clients.Group(TaskCollaborationHub.TaskGroup(taskId)).SendAsync("TaskChanged", taskId);

        public Task AttachmentsChangedAsync(int taskId) =>
            _hub.Clients.Group(TaskCollaborationHub.TaskGroup(taskId)).SendAsync("TaskAttachmentsChanged");

        public Task WorkspaceRosterChangedAsync(int workspaceId, IReadOnlyList<int> taskIds) =>
            _hub.Clients.Groups(taskIds.Select(TaskCollaborationHub.TaskGroup).ToArray())
                .SendAsync("WorkspaceRosterChanged", workspaceId);

        public async Task AccessRevokedAsync(int taskId, int userId)
        {
            foreach (var connectionId in TaskCollaborationHub.ConnectionsFor(taskId, userId))
            {
                await _hub.Groups.RemoveFromGroupAsync(connectionId, TaskCollaborationHub.TaskGroup(taskId));
                TaskCollaborationHub.Forget(taskId, userId, connectionId);
                await _hub.Clients.Client(connectionId).SendAsync("TaskAccessRevoked", taskId);
            }
        }
    }
}
