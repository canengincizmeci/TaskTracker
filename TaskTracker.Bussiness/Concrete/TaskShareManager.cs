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
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;

        public TaskShareManager(IUnitOfWork unitOfWork, ITaskShareDal taskShareDal, ICurrentUserService currentUserService, IEmailService emailService, INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _taskShareDal = taskShareDal;
            _currentUserService = currentUserService;
            _emailService = emailService;
            _notificationService = notificationService;
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
