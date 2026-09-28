using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Entities.DTOs;

public class CreateWorkspaceDto
{
    public string Name { get; set; } = string.Empty;
}

public class RenameWorkspaceDto
{
    public string Name { get; set; } = string.Empty;
    public long Version { get; set; }
}

public class WorkspaceListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public WorkspaceRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WorkspaceDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long Version { get; set; }
    public WorkspaceRole CurrentUserRole { get; set; }
    public int MemberCount { get; set; }
    public List<WorkspaceMemberDto> Members { get; set; } = [];
}

public class WorkspaceMemberDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public WorkspaceRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
    public long Version { get; set; }
}

public class CreateWorkspaceInvitationDto
{
    public string Username { get; set; } = string.Empty;
}

public class WorkspaceInvitationDto
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = string.Empty;
    public int InvitedUserId { get; set; }
    public string InvitedUserName { get; set; } = string.Empty;
    public string InvitedByUserName { get; set; } = string.Empty;
    public WorkspaceInvitationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public long Version { get; set; }
}

public class ChangeWorkspaceMemberRoleDto
{
    public WorkspaceRole Role { get; set; }
    public long Version { get; set; }
}

public class WorkspaceVersionDto
{
    public long Version { get; set; }
}
