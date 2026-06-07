//using Castle.Core.Configuration;
using System;
using Microsoft.Extensions.Configuration;
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
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;
        private readonly IConfiguration _configuration;

        public TaskShareManager(IUnitOfWork unitOfWork, ITaskShareDal taskShareDal, ICurrentUserService currentUserService, IEmailService emailService, INotificationService notificationService, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _taskShareDal = taskShareDal;
            _currentUserService = currentUserService;
            _emailService = emailService;
            _notificationService = notificationService;
            _configuration = configuration;
        }


        public async Task<IResult> AcceptTaskInvitationAsync(int invitationId)
        {
            var invitationRepository = _unitOfWork.GetRepository<TaskShareInvitation>();

            var invitation = await invitationRepository.GetByIdAsync(invitationId);

            if (invitation is null)
                return new ErrorResult(Messages.InvitationNotFound);

            if (invitation.InvitedUserId != _currentUserService.UserId)
                return new ErrorResult(Messages.AuthorizationDenied);

            if (invitation.Status != TaskShareInvitationStatus.Pending)
                return new ErrorResult(Messages.InvitationAlreadyResponded);

            if (invitation.ExpiresAt.HasValue && invitation.ExpiresAt.Value < DateTime.UtcNow)
                return new ErrorResult(Messages.InvitationExpired);

            var taskAlreadyShared = await _taskShareDal.GetAsync(x =>
                x.TaskRequestId == invitation.TaskRequestId &&
                x.SharedWithUserId == invitation.InvitedUserId);

            if (taskAlreadyShared is not null)
                return new ErrorResult(Messages.TaskAlreadyShared);

            await _taskShareDal.AddAsync(new TaskShare
            {
                TaskRequestId = invitation.TaskRequestId,
                SharedWithUserId = invitation.InvitedUserId,
                Permission = invitation.Permission,
                SharedAt = DateTime.UtcNow
            });

            invitation.Status = TaskShareInvitationStatus.Accepted;
            invitation.RespondedAt = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult(Messages.TaskAccepted);
        }

        //public async Task<IDataResult<List<TaskInvitationDto>>> GetMyPendingInvitationsAsync()
        //{
        //    int? userId = _currentUserService.UserId;
        //    if (!userId.HasValue)
        //    {
        //        return new ErrorDataResult<List<TaskInvitationDto>>(Messages.UserNotFound);
        //    }

        //    var invitationRepository = _unitOfWork.GetRepository<TaskShareInvitation>();
        //    var invitations = await invitationRepository.GetAllAsync(
        //        x => x.InvitedUserId == userId.Value && x.Status == TaskShareInvitationStatus.Pending);

        //    if (invitations is null)
        //    {
        //        return new ErrorDataResult<List<TaskInvitationDto>>(Messages.InvitationNotFound);

        //    }



        //    List<TaskShareInvitation> taskShareInvitations = new List<TaskShareInvitation>();
        //    foreach (var item in invitations)
        //    {
        //        taskShareInvitations.Add(new TaskShareInvitation
        //        {
        //            Id=item.Id,
        //            TaskRequestId=item.TaskRequestId,
        //            CreatedAt=item.CreatedAt,
        //            ExpiresAt=item.ExpiresAt,
        //            InvitedByUserId=item.InvitedUserId,
        //            Permission = item.Permission,
        //            RespondedAt=item.RespondedAt,
        //            Status=item.Status
        //        });

        //    }

        //    return new SuccessDataResult<List<TaskInvitationDto>>(invitations);
        //}

        public async Task<IDataResult<List<TaskInvitationDto>>> GetMyPendingInvitationsAsync()
        {
            var userId = _currentUserService.UserId;

            var invitationRepository = _unitOfWork.GetRepository<TaskShareInvitation>();
            var taskRepository = _unitOfWork.GetRepository<TaskRequest>();
            var userRepository = _unitOfWork.GetRepository<User>();

            var invitations = await invitationRepository.GetAllAsync(x =>
                x.InvitedUserId == userId &&
                x.Status == TaskShareInvitationStatus.Pending);

            var invitationDtos = new List<TaskInvitationDto>();

            foreach (var invitation in invitations)
            {
                var task = await taskRepository.GetByIdAsync(invitation.TaskRequestId);
                var inviter = await userRepository.GetByIdAsync(invitation.InvitedByUserId);

                invitationDtos.Add(new TaskInvitationDto
                {
                    Id = invitation.Id,
                    TaskRequestId = invitation.TaskRequestId,
                    TaskTitle = task?.Title ?? "Unknown Task",
                    InviterUserName = inviter?.UserName ?? "Unknown User",
                    Permission = invitation.Permission,
                    CreatedAt = invitation.CreatedAt,
                    ExpiresAt = invitation.ExpiresAt
                });
            }

            return new SuccessDataResult<List<TaskInvitationDto>>(invitationDtos);
        }

        public async Task<IDataResult<List<SharedTaskDto>>> GetMySharedTasksAsync()
        {
            int? userId = _currentUserService.UserId;
            if (!userId.HasValue)
            {
                return new ErrorDataResult<List<SharedTaskDto>>(Messages.AuthorizationDenied);
            }

            var sharedTasks = _taskShareDal.GetAllAsync(p => p.SharedWithUserId == userId);

            if (sharedTasks is null)
            {
                return new SuccessDataResult<List<SharedTaskDto>>(Messages.DataNotFound);
            }

            return new SuccessDataResult<List<SharedTaskDto>>(Messages.DataListed);
        }

        public async Task<IResult> InviteUserToTask(InviteUserToTaskDto dto)
        {
            var taskRepository = _unitOfWork.GetRepository<TaskRequest>();
            var userRepository = _unitOfWork.GetRepository<User>();
            var invitationRepository = _unitOfWork.GetRepository<TaskShareInvitation>();

            var user = await userRepository.GetAsync(u => u.UserName == dto.Username);


            if (user is null)
                return new ErrorResult(Messages.UserNotFound);

            var task = await taskRepository.GetByIdAsync(dto.TaskRequestId);

            if (task is null)
                return new ErrorResult(Messages.DataNotFound);

            var currentUserId = _currentUserService.UserId;

            if (task.OwnerId != currentUserId)
                return new ErrorResult(Messages.AuthorizationDenied);

            if (task.OwnerId == user.Id)
                return new ErrorResult(Messages.UserCannotShareTaskWithSelf);

            var taskAlreadyShared = await _taskShareDal.GetAsync(x =>
                x.TaskRequestId == dto.TaskRequestId &&
                x.SharedWithUserId == user.Id);

            if (taskAlreadyShared is not null)
                return new ErrorResult(Messages.TaskAlreadyShared);

            var pendingInvitation = await invitationRepository.GetAsync(x =>
                x.TaskRequestId == dto.TaskRequestId &&
                x.InvitedUserId == user.Id &&
                x.Status == TaskShareInvitationStatus.Pending);

            if (pendingInvitation is not null)
                return new ErrorResult(Messages.TaskShareInvitationAlreadySent);
            var inviter = await userRepository.GetByIdAsync(currentUserId);
            if (inviter is null)
                return new ErrorResult(Messages.UserNotFound);
            var invitation = new TaskShareInvitation
            {
                TaskRequestId = dto.TaskRequestId,
                InvitedUserId = user.Id,
                InvitedByUserId = currentUserId,
                Permission = dto.Permission,
                Status = TaskShareInvitationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };


            await invitationRepository.AddAsync(invitation);

            await _unitOfWork.SaveChangesAsync();



            await _notificationService.CreateTaskShareInvitationNotificationAsync(user.Id, task.Title, inviter.UserName, invitation.Id);

            try
            {
                var clientBaseUrl = _configuration["ClientApp:BaseUrl"];

                var invitationUrl = $"{clientBaseUrl}/tasks/task-detail/{task.Id}";

                await _emailService.SendTaskShareInvitationEmailAsync(user.Email, task.Title, inviter.UserName, $"https://canncodehub.com/invitations/{invitation.Id}");
            }
            catch (Exception)
            {


            }

            return new SuccessResult(Messages.TaskShareInvitationSent);
        }

        public async Task<IResult> RejectTaskInvitationAsync(int invitationId)
        {
            var invitationRepository = _unitOfWork.GetRepository<TaskShareInvitation>();

            var invitation = await invitationRepository.GetByIdAsync(invitationId);

            if (invitation is null)
                return new ErrorResult(Messages.InvitationNotFound);

            if (invitation.InvitedUserId != _currentUserService.UserId)
                return new ErrorResult(Messages.AuthorizationDenied);

            if (invitation.Status != TaskShareInvitationStatus.Pending)
                return new ErrorResult(Messages.InvitationAlreadyResponded);

            invitation.Status = TaskShareInvitationStatus.Rejected;
            invitation.RespondedAt = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult(Messages.TaskRejected);
        }
    }
}
