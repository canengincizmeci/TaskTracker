using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete;

public class WorkspaceMember : IEntity
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public int UserId { get; set; }
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;
    public bool IsActive { get; set; } = true;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RemovedAt { get; set; }
    public long Version { get; private set; }
    public Workspace Workspace { get; set; } = null!;
    public User User { get; set; } = null!;
}
