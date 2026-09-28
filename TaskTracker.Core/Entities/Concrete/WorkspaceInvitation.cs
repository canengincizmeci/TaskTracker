using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete;

public class WorkspaceInvitation : IEntity
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public int InvitedUserId { get; set; }
    public int InvitedByUserId { get; set; }
    public WorkspaceInvitationStatus Status { get; set; } = WorkspaceInvitationStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public long Version { get; private set; }
    public Workspace Workspace { get; set; } = null!;
    public User InvitedUser { get; set; } = null!;
    public User InvitedByUser { get; set; } = null!;
}
