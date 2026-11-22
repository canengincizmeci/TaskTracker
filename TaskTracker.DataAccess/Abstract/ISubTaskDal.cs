using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.DataAccess.Abstract;

public interface ISubTaskDal
{
    Task LockParentAsync(int taskId, bool exclusive, CancellationToken ct = default);
    Task LockParentWorkspaceAsync(int taskId, CancellationToken ct = default);
    Task LockWorkspaceForMemberRemovalAsync(int workspaceId, CancellationToken ct = default);
    Task LockSubTaskForReadAsync(int taskId, int subTaskId, CancellationToken ct = default);
    Task<TaskRequest?> GetActiveParentAsync(int taskId, CancellationToken ct = default);
    Task<SubTask?> GetActiveAsync(int taskId, int subTaskId, CancellationToken ct = default);
    Task<bool> HasActiveAsync(int taskId, int subTaskId, CancellationToken ct = default);
    Task<SubTaskDto> GetDetailAsync(int taskId, int subTaskId, int userId, CancellationToken ct = default);
    Task<List<SubTaskDto>> ListAsync(int taskId, int userId, CancellationToken ct = default);
    Task<List<AssignedSubTaskDto>> ListAssignedAsync(int userId);
    Task<bool> HasOpenAsync(int taskId);
    Task<List<SubTask>> GetOpenTaskAssignmentsAsync(int taskId, int userId);
    Task<List<SubTask>> GetOpenWorkspaceAssignmentsAsync(int workspaceId, int userId);
    Task<SubTaskSubmission?> GetLatestSubmissionAsync(int taskId, int subTaskId);
    Task<List<SubTaskSubmissionDto>> GetSubmissionsAsync(int taskId, int subTaskId, CancellationToken ct = default);
    Task<List<SubTaskActivityDto>> GetActivitiesAsync(int taskId, int subTaskId, CancellationToken ct = default);
}
