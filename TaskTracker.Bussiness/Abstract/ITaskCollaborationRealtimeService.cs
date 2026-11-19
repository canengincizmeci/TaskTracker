using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract;

public interface ITaskCollaborationRealtimeService
{
    Task ActivityCreatedAsync(int taskId, TaskActivityDto activity);
    Task MessageCreatedAsync(int taskId, TaskMessageDto message);
    Task TaskChangedAsync(int taskId);
    Task AttachmentsChangedAsync(int taskId);
    Task WorkspaceRosterChangedAsync(int workspaceId, IReadOnlyList<int> taskIds);
    Task AccessRevokedAsync(int taskId, int userId);
}
