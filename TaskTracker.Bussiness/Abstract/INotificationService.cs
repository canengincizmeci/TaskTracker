using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Bussiness.Abstract
{
    public interface INotificationService
    {
        Task CreateTaskShareInvitationNotificationAsync(int userId, string taskTitle, string inviterUserName, int invitationId);
        Task CreateTaskNotificationAsync(int userId, NotificationType type, string title, string message, int taskId,
            string? redirectUrl = null);
        Task<IDataResult<List<NotificationDto>>> GetNotificationsForUserAsync(int userId);
        Task<IResult> MarkAsReadAsync(int notificationId);
        Task<IResult> MarkAllAsReadAsync();
        //Task<IResult> GetUserPendingInivationAsync();

    }
}
