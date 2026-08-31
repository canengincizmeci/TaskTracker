using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract
{
    public interface IRealtimeNotificationService
    {
        Task SendNotificationAsync(int userId, NotificationDto notification);
    }
}
