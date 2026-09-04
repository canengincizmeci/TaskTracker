using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class ChangePasswordDto : IDto
    {
        public string CurrentPassword { get; set; } = null!;
        public string NewPassword { get; set; } = null!;
        public string ConfirmNewPassword { get; set; } = null!;
    }
}
