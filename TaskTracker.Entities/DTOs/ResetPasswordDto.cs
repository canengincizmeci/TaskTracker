using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class ResetPasswordDto : IDto
    {
        public string ResetToken { get; set; } = null!;
        public string NewPassword { get; set; } = null!;
        public string ConfirmNewPassword { get; set; } = null!;
    }
}
