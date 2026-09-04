using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class VerifyPasswordResetCodeDto : IDto
    {
        public string Email { get; set; } = null!;
        public string Code { get; set; } = null!;
    }
}
