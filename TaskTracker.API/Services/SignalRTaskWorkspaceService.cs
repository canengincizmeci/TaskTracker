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
    }
}
