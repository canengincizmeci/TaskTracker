using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.DataAccess.Abstract;

public interface IWorkspaceDal
{
    Task<List<WorkspaceListItemDto>> GetForUserAsync(int userId);
    Task<WorkspaceDetailDto?> GetDetailsAsync(int workspaceId, int userId);
    Task<List<WorkspaceMemberDto>> GetMembersAsync(int workspaceId);
    Task<List<WorkspaceInvitationDto>> GetInvitationsAsync(int workspaceId);
    Task<List<WorkspaceInvitationDto>> GetPendingInvitationsForUserAsync(int userId, DateTime now);
    Task<Workspace?> GetWorkspaceAsync(int workspaceId);
    Task<WorkspaceMember?> GetActiveMembershipAsync(int workspaceId, int userId);
    Task<WorkspaceMember?> GetMembershipAsync(int workspaceId, int userId);
    Task<WorkspaceInvitation?> GetInvitationAsync(int invitationId);
    Task<WorkspaceInvitation?> GetPendingInvitationAsync(int workspaceId, int invitedUserId);
    Task<User?> GetUserByUsernameAsync(string username);
    void TouchMembership(WorkspaceMember membership);
}
