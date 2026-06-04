using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract
{
    public interface INotificationService
    {
        Task CreateTaskShareInvitationNotificationAsync(int userId, string taskTitle, string inviterUserName, int invitationId);
        Task<IDataResult<List<NotificationDto>>> GetNotificationsForUserAsync(int userId);
        Task<IResult> MarkAsReadAsync(int notificationId);
        Task<IResult> MarkAllAsReadAsync();
        //Task<IResult> GetUserPendingInivationAsync();

    }
}
