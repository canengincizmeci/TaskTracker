using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Bussiness.Concrete
{
    public class NotificationManager : INotificationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;


        public NotificationManager(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
        {
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;

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
    }
}
