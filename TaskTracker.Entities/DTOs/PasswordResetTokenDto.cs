using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class PasswordResetTokenDto : IDto
    {
        public string ResetToken { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
    }
}
