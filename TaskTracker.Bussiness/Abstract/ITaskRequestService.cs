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
        Task<IDataResult<TaskRequest>> GetTaskById(int taskId, int currentUserId);
        Task<IResult> DeleteTask(int taskId, int currentUserId);
        Task<IResult> UpdateTask(UpdateTaskRequestDto taskRequest, int currentUserId);
        Task<IDataResult<List<TaskRequest>>> GetAllTasks();
        Task<IDataResult<List<GetTasksDto>>> GetTasksByUserId(int userId);



    }
}
