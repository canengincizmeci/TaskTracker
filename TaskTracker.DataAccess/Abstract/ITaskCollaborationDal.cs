using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.DataAccess.Abstract;

public interface ITaskCollaborationDal
{
    Task<bool> CanAccessAsync(int taskId, int userId);
    Task<List<TaskActivity>> GetActivitiesAsync(int taskId);
    Task<TaskActivity?> GetActivityAsync(int activityId);
    Task<List<TaskMessage>> GetMessagesAsync(int taskId);
}
