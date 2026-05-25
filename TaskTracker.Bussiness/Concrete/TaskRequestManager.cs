using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Bussiness.ValidationRules.FluentValidation;
using TaskTracker.Core.Aspects.Autofac;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Concrete
{
    public class TaskRequestManager : ITaskRequestService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITaskShareDal _taskShareDal;

        public TaskRequestManager(IUnitOfWork unitOfWork, ITaskShareDal taskShareDal)
        {
            _unitOfWork = unitOfWork;
            _taskShareDal = taskShareDal;
        }

        [ValidationAspect(typeof(TaskRequestCreateDtoValidator))]
        public async Task<IResult> AddTaskRequestAsync(TaskRequestCreateDto dto, int currentUserId)
        {
            var taskRepository = _unitOfWork.GetRepository<TaskRequest>();

            var taskRequest = new TaskRequest
            {
                Title = dto.Title,
                Description = dto.Description,
                Category = dto.Category,
                Priority = dto.Priority,
                Status = dto.Status,
                DueDate = dto.DueDate,
                OwnerId = currentUserId,
                Activity = true,
                Visibility = TaskVisibility.Private,
                CreatedAt = DateTime.UtcNow,
                SharedCount = 0
            };

            await taskRepository.AddAsync(taskRequest);
            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult(Messages.DataAdded);
        }

        public async Task<IResult> DeleteTask(int taskId, int currentUserId)
        {
            var taskRepository = _unitOfWork.GetRepository<TaskRequest>();

            var task = await taskRepository.GetByIdAsync(taskId);

            if (task == null)
                return new ErrorResult(Messages.DataNotFound);

            var canManage = task.OwnerId == currentUserId || await _taskShareDal.HasPermissionAsync(taskId, currentUserId, TaskPermission.Manage);

            if (!canManage)
                return new ErrorResult(Messages.AuthorizationDenied);

            task.Activity = false;

            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult(Messages.DataUpdated);
        }

        public async Task<IDataResult<TaskRequest>> GetTaskById(int taskId, int currentUserId)
        {
            var taskRepository = _unitOfWork.GetRepository<TaskRequest>();
            var task = await taskRepository.GetByIdAsync(taskId);

            if (task == null || !task.Activity)
                return new ErrorDataResult<TaskRequest>(Messages.DataNotFound);

            var canView = task.OwnerId == currentUserId || task.Visibility == TaskVisibility.Public || await _taskShareDal.HasPermissionAsync(taskId, currentUserId, TaskPermission.View);

            if (!canView)
                return new ErrorDataResult<TaskRequest>(Messages.AuthorizationDenied);

            return new SuccessDataResult<TaskRequest>(task);
        }

        public async Task<IResult> ShareTask(int taskId, int ownerUserId, int sharedUserId, TaskPermission permission)
        {
            var taskRepository = _unitOfWork.GetRepository<TaskRequest>();
            var userRepository = _unitOfWork.GetRepository<User>();

            var user = await userRepository.GetByIdAsync(sharedUserId);
            if (user == null)
                return new ErrorResult(Messages.UserNotFound);

            var task = await taskRepository.GetByIdAsync(taskId);
            if (task == null || !task.Activity)
                return new ErrorResult(Messages.DataNotFound);

            if (task.OwnerId != ownerUserId)
                return new ErrorResult(Messages.AuthorizationDenied);

            if (ownerUserId == sharedUserId)
                return new ErrorResult("Task kendinle paylaşılamaz.");

            var alreadyShared = await _taskShareDal.GetAsync(x => x.TaskRequestId == taskId && x.SharedWithUserId == sharedUserId);

            if (alreadyShared != null)
            {
                alreadyShared.Permission = permission;
                alreadyShared.SharedAt = DateTime.UtcNow;

                _taskShareDal.Update(alreadyShared);
            }
            else
            {
                var newShare = new TaskShare
                {
                    TaskRequestId = taskId,
                    SharedWithUserId = sharedUserId,
                    Permission = permission,
                    SharedAt = DateTime.UtcNow
                };

                await _taskShareDal.AddAsync(newShare);
            }


            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult(Messages.DataAdded);
        }

        [ValidationAspect(typeof(UpdateTaskRequestDtoValidator))]
        public async Task<IResult> UpdateTask(UpdateTaskRequestDto taskRequest, int currentUserId)
        {
            var taskRepository = _unitOfWork.GetRepository<TaskRequest>();

            var task = await taskRepository.GetByIdAsync(taskRequest.Id);

            if (task == null || !task.Activity)
                return new ErrorResult(Messages.DataNotFound);

            var canEdit =
                task.OwnerId == currentUserId ||
                await _taskShareDal.HasPermissionAsync(
                    task.Id,
                    currentUserId,
                    TaskPermission.Edit);

            if (!canEdit)
                return new ErrorResult(Messages.AuthorizationDenied);

            task.Title = taskRequest.Title;
            task.Description = taskRequest.Description;
            task.Category = taskRequest.Category;
            task.Priority = taskRequest.Priority;
            task.Status = taskRequest.Status;
            task.DueDate = taskRequest.DueDate;

            taskRepository.Update(task);

            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult(Messages.DataUpdated);
        }
        public async Task<IDataResult<List<TaskRequest>>> GetAllTasks()
        {
            var taskRepository = _unitOfWork.GetRepository<TaskRequest>();

            var tasks = await taskRepository.GetAllAsync(x => x.Activity);

            return new SuccessDataResult<List<TaskRequest>>(tasks);
        }
    }
}
