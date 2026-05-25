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
        Task<IResult> ShareTask(int taskId, int ownerUserId, int sharedUserId, TaskPermission permission);
        Task<IDataResult<List<TaskRequest>>> GetAllTasks();





    }
}
