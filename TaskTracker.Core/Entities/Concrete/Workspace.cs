namespace TaskTracker.Core.Entities.Concrete;

public class Workspace : IEntity
{
    public const int MaxNameLength = 150;

    public int Id { get; set; }
    public required string Name { get; set; }
    public long Version { get; private set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<WorkspaceMember> Members { get; set; } = new List<WorkspaceMember>();
    public ICollection<WorkspaceInvitation> Invitations { get; set; } = new List<WorkspaceInvitation>();
    public ICollection<WorkspaceActivity> Activities { get; set; } = new List<WorkspaceActivity>();
}
