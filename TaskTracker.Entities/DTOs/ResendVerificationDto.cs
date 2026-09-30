using TaskTracker.Core.Entities;

namespace TaskTracker.Entities.DTOs;

public sealed class ResendVerificationDto : IDto
{
    public string Email { get; set; } = null!;
}
