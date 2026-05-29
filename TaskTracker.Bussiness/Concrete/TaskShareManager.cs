using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Concrete
{
    public class TaskShareManager : ITaskShareService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITaskShareDal _taskShareDal;
        private readonly ICurrentUserService _currentUserService;

        public TaskShareManager(IUnitOfWork unitOfWork, ITaskShareDal taskShareDal, ICurrentUserService currentUserService)
        {
            _unitOfWork = unitOfWork;
            _taskShareDal = taskShareDal;
            _currentUserService = currentUserService;
        }

        public async Task<IResult> InviteUserToTask(InviteUserToTaskDto dto)
        {
            var taskRepository = _unitOfWork.GetRepository<TaskRequest>();
            var userRepository = _unitOfWork.GetRepository<User>();

            var user = await userRepository.GetAsync(u => u.UserName == dto.Username);

            if (user is null)
            {
                return new ErrorResult(Messages.UserNotFound);
            }

            var task = await taskRepository.GetByIdAsync(dto.TaskRequestId);

            if (task is null)
            {
                return new ErrorResult(Messages.DataNotFound);
            }

            var currentUserId = _currentUserService.UserId;

            if (task.OwnerId != currentUserId)
                return new ErrorResult(Messages.AuthorizationDenied);

            if (task.OwnerId == user.Id)
                return new ErrorResult(Messages.UserCannotShareTaskWithSelf);

            var taskAlreadyShared = await _taskShareDal.GetAsync(x => x.TaskRequestId == dto.TaskRequestId && x.SharedWithUserId == user.Id);

            if (taskAlreadyShared is not null)
            {
                return new ErrorResult(Messages.TaskAlreadyShared);
            }


            await _taskShareDal.AddAsync(new TaskShare
            {
                TaskRequestId = dto.TaskRequestId,
                SharedWithUserId = user.Id,
                Permission = dto.Permission,
                SharedAt = DateTime.UtcNow,
            });

            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult(Messages.TaskShared);
        }
    
    }
}
