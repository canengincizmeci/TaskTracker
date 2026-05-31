using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Bussiness.Abstract;

namespace TaskTracker.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly ICurrentUserService _currentUserService;

        public NotificationController(INotificationService notificationService, ICurrentUserService currentUserService)
        {
            _notificationService = notificationService;
            _currentUserService = currentUserService;
        }

        [HttpGet("user-notifications")]
        [Authorize(Roles = "Admin,User")]
        public async Task<IActionResult> UserNotifications()
        {
            var currentUserId = _currentUserService.UserId;
            var result = await _notificationService.GetNotificationsForUserAsync(currentUserId);

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result.Data);
        }

        [HttpPost("mark-as-read/{notificationId}")]
        [Authorize(Roles = "Admin,User")]
        public async Task<IActionResult> MarkAsRead(int notificationId)
        {
            var result = await _notificationService.MarkAsReadAsync(notificationId);
            if (!result.Success)
                return BadRequest(result.Message);
            return Ok(result.Message);
        }



        [HttpPost("mark-all-as-read")]
        [Authorize(Roles = "Admin,User")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var currentUserId = _currentUserService.UserId;
            var result = await _notificationService.MarkAllAsReadAsync();
            if (!result.Success)
                return BadRequest(result.Message);
            return Ok(result.Message);
        }
    }
}
