using System;
using System.Collections.Generic;
using System.Text;

namespace TaskTracker.Bussiness.Abstract
{
    public interface IRealtimeNotificationService
    {
        Task SendNotificationAsync(int userId, string message);
    }
}
