using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TaskTracker.Bussiness.Abstract;

namespace TaskTracker.API.Hubs
{
    [Authorize]
    public class TaskWorkspaceHub : Hub
    {
        private readonly ITaskWorkspaceService _workspace;

        public TaskWorkspaceHub(ITaskWorkspaceService workspace)
        {
            _workspace = workspace;
        }

        public static string TaskGroup(int taskId) => $"task:{taskId}";

        public async Task JoinTask(int taskId)
        {
            if (!int.TryParse(Context.UserIdentifier, out var userId) ||
                !await _workspace.CanAccessAsync(taskId, userId))
                throw new HubException("You do not have access to this task workspace.");
            await Groups.AddToGroupAsync(Context.ConnectionId, TaskGroup(taskId));
        }

        public Task LeaveTask(int taskId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskGroup(taskId));
    }
}
