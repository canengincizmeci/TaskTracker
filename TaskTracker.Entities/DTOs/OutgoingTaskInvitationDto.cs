namespace TaskTracker.Entities.DTOs;

public class OutgoingTaskInvitationDto
{
    public int Id { get; set; }
    public int TaskRequestId { get; set; }
    public string InvitedUserName { get; set; } = null!;
    public string? Permission { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool CanCancel { get; set; }
}
