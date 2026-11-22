using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract;

public interface ITaskCollaborationService
{
    Task<bool> CanAccessAsync(int taskId, int userId, CancellationToken ct = default);
    Task<IDataResult<List<TaskActivityDto>>> GetActivitiesAsync(int taskId, int userId);
    Task<IDataResult<List<TaskMessageDto>>> GetMessagesAsync(int taskId, int userId);
    Task<IDataResult<TaskMessageDto>> SendMessageAsync(int taskId, int userId, string? content);
    Task PublishMessageAsync(int taskId, TaskMessageDto message);
    Task PublishActivityAsync(TaskActivity activity);
    Task PublishTaskChangedAsync(int taskId);
    Task PublishAttachmentsChangedAsync(int taskId);
    Task PublishWorkspaceRosterChangedAsync(int workspaceId, IReadOnlyList<int> taskIds);
    Task RevokeAccessAsync(int taskId, int userId);
}
