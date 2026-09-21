using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract;

public interface ITaskWorkspaceService
{
    Task<bool> CanAccessAsync(int taskId, int userId);
    Task<IDataResult<List<TaskActivityDto>>> GetActivitiesAsync(int taskId, int userId);
    Task<IDataResult<List<TaskMessageDto>>> GetMessagesAsync(int taskId, int userId);
    Task<IDataResult<TaskMessageDto>> SendMessageAsync(int taskId, int userId, string? content);
    Task PublishActivityAsync(TaskActivity activity);
    Task PublishTaskChangedAsync(int taskId);
    Task RevokeAccessAsync(int taskId, int userId);
}
