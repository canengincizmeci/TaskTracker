using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract;

public interface ITaskWorkspaceRealtimeService
{
    Task ActivityCreatedAsync(int taskId, TaskActivityDto activity);
    Task MessageCreatedAsync(int taskId, TaskMessageDto message);
}
