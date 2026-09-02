using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs
{
    public class ForgotPasswordDto : IDto
    {
        public string Email { get; set; } = null!;
    }
}
