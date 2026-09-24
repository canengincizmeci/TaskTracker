using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract
{
    public interface ITaskRequestService 
    {
        Task<IResult> AddTaskRequestAsync(TaskRequestCreateDto dto,int currentUserId);
        Task<IDataResult<TaskRequestDto>> GetTaskById(int taskId, int currentUserId);
        Task<IResult> DeleteTask(int taskId, int currentUserId);
        Task<IResult> UpdateTask(UpdateTaskRequestDto taskRequest, int currentUserId);
        Task<IResult> AssignTaskAsync(int taskId, AssignTaskDto dto, int currentUserId);
        Task<IResult> UnassignTaskAsync(int taskId, TaskWorkflowCommandDto dto, int currentUserId);
        Task<IResult> StartTaskAsync(int taskId, TaskWorkflowCommandDto dto, int currentUserId);
        Task<IResult> CompleteTaskAsync(int taskId, TaskWorkflowCommandDto dto, int currentUserId);
        Task<IResult> CancelTaskAsync(int taskId, TaskWorkflowCommandDto dto, int currentUserId);
        Task<IResult> ReopenTaskAsync(int taskId, TaskWorkflowCommandDto dto, int currentUserId);
        Task<IDataResult<List<TaskRequest>>> GetAllTasks();
        Task<IDataResult<List<GetTasksDto>>> GetTasksByUserId(int userId);
        Task<IDataResult<List<GetTasksDto>>> GetAssignedTasksAsync(int userId);
        Task<IDataResult<TaskSubmissionDto>> SubmitAsync(int taskId, CreateTaskSubmissionDto dto, int currentUserId);
        Task<IDataResult<List<TaskSubmissionDto>>> GetSubmissionHistoryAsync(int taskId, int currentUserId);
        Task<IDataResult<TaskSubmissionReviewDto>> ReviewAsync(int taskId, int submissionId,
            ReviewTaskSubmissionDto dto, int currentUserId);
        Task<IDataResult<List<AwaitingReviewTaskDto>>> GetAwaitingReviewAsync(int currentUserId);



    }
}
