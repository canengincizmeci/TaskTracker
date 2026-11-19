namespace TaskTracker.Entities.DTOs;

public sealed class RespondToTaskInvitationDto
{
    public bool Accept { get; set; }
    public string? ResponseMessage { get; set; }
}
