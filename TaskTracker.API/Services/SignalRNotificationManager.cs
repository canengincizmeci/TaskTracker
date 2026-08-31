using Microsoft.AspNetCore.SignalR;
using TaskTracker.API.Hubs;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.API.Services
{
    public class SignalRNotificationManager : IRealtimeNotificationService
    {
        private readonly IHubContext<NotificationHub> _hubContext;

        public SignalRNotificationManager(
            IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext;
        }
        public async Task SendNotificationAsync(int userId, NotificationDto notification)
        {
            await _hubContext.Clients.User(userId.ToString()).SendAsync("ReceiveNotification", notification);
        }
    }
}
