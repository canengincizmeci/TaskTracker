using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Concrete
{
    public class NotificationManager : INotificationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly IRealtimeNotificationService _realtimeNotificationService;


        public NotificationManager(IUnitOfWork unitOfWork, ICurrentUserService currentUserService, IRealtimeNotificationService realtimeNotificationService)
        {
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _realtimeNotificationService = realtimeNotificationService;
        }

        public async Task CreateTaskShareInvitationNotificationAsync(int userId, string taskTitle, string inviterUserName, int invitationId)
        {
            var notificationRepository = _unitOfWork.GetRepository<Notification>();

            await notificationRepository.AddAsync(new Notification
            {
                UserId = userId,
                Type = NotificationType.TaskShareInvitation,
                Title = "New Task Invitation",
                Message = $"{inviterUserName} invited you to collaborate on '{taskTitle}'.",
                RelatedEntityId = invitationId,
                RedirectUrl = $"/tasks/invitations/{invitationId}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<IDataResult<List<NotificationDto>>> GetNotificationsForUserAsync(int userId)
        {
            var notificationRepository = _unitOfWork.GetRepository<Notification>();

            var notifications = await notificationRepository.GetAllAsync(n => n.UserId == userId);

            var notificationDtos = notifications.OrderByDescending(n => n.CreatedAt)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    UserId = n.UserId,
                    Type = n.Type,
                    Title = n.Title,
                    Message = n.Message,
                    IsRead = n.IsRead,
                    RelatedEntityId = n.RelatedEntityId,
                    RedirectUrl = n.RedirectUrl,
                    CreatedAt = n.CreatedAt,
                    ReadAt = n.ReadAt
                })
                .ToList();

            return new SuccessDataResult<List<NotificationDto>>(notificationDtos);
        }


        public async Task<IResult> MarkAsReadAsync(int notificationId)
        {
            var notificationRepository = _unitOfWork.GetRepository<Notification>();
            var notification = await notificationRepository.GetAsync(n => n.Id == notificationId && n.UserId == _currentUserService.UserId);

            if (notification is null)
            {
                return new ErrorResult(Messages.NotificationNotFound);
            }

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();

            return new SuccessResult();
        }

        public async Task<IResult> MarkAllAsReadAsync()
        {
            var notificationRepository = _unitOfWork.GetRepository<Notification>();
            var unreadNotifications = await notificationRepository.GetAllAsync(nt =>
                nt.UserId == _currentUserService.UserId && !nt.IsRead);
            var readAt = DateTime.UtcNow;

            foreach (var notification in unreadNotifications)
            {
                notification.IsRead = true;
                notification.ReadAt = readAt;
            }

            await _unitOfWork.SaveChangesAsync();
            return new SuccessResult();
        }
    }
}
