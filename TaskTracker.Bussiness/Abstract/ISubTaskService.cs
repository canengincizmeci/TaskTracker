using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract;

public interface ISubTaskService
{
    Task<IDataResult<SubTaskDto>> CreateAsync(int taskId, CreateSubTaskDto dto, int userId);
    Task<IDataResult<List<SubTaskDto>>> ListAsync(int taskId, int userId);
    Task<IDataResult<List<AssignedSubTaskDto>>> ListAssignedAsync(int userId);
    Task<IDataResult<SubTaskDto>> GetAsync(int taskId, int subTaskId, int userId);
    Task<IDataResult<SubTaskDto>> UpdateAsync(int taskId, int subTaskId, UpdateSubTaskDto dto, int userId);
    Task<IResult> DeleteAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId);
    Task<IDataResult<SubTaskDto>> AssignAsync(int taskId, int subTaskId, AssignSubTaskDto dto, int userId);
    Task<IDataResult<SubTaskDto>> UnassignAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId);
    Task<IDataResult<SubTaskDto>> StartAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId);
    Task<IDataResult<SubTaskDto>> CompleteAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId);
    Task<IDataResult<SubTaskDto>> CancelAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId);
    Task<IDataResult<SubTaskDto>> ReopenAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId);
    Task<IDataResult<SubTaskDto>> SubmitAsync(int taskId, int subTaskId, CreateSubTaskSubmissionDto dto, int userId);
    Task<IDataResult<SubTaskDto>> ReviewAsync(int taskId, int subTaskId, int submissionId, ReviewSubTaskSubmissionDto dto,
        int userId);
    Task<IDataResult<List<SubTaskSubmissionDto>>> GetSubmissionHistoryAsync(int taskId, int subTaskId, int userId);
    Task<IDataResult<List<SubTaskActivityDto>>> GetActivityHistoryAsync(int taskId, int subTaskId, int userId);
}
