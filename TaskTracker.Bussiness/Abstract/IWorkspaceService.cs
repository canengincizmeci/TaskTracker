using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Abstract;

public interface IWorkspaceService
{
    Task<IDataResult<WorkspaceDetailDto>> CreateAsync(CreateWorkspaceDto dto);
    Task<IDataResult<List<WorkspaceListItemDto>>> GetMineAsync();
    Task<IDataResult<WorkspaceDetailDto>> GetDetailsAsync(int workspaceId);
    Task<IDataResult<List<WorkspaceMemberDto>>> GetMembersAsync(int workspaceId);
    Task<IResult> RenameAsync(int workspaceId, RenameWorkspaceDto dto);
    Task<IDataResult<WorkspaceInvitationDto>> InviteAsync(int workspaceId, CreateWorkspaceInvitationDto dto);
    Task<IDataResult<List<WorkspaceInvitationDto>>> GetInvitationsAsync(int workspaceId);
    Task<IDataResult<List<WorkspaceInvitationDto>>> GetMyInvitationsAsync();
    Task<IResult> AcceptInvitationAsync(int invitationId, WorkspaceVersionDto dto);
    Task<IResult> RejectInvitationAsync(int invitationId, WorkspaceVersionDto dto);
    Task<IResult> CancelInvitationAsync(int workspaceId, int invitationId, WorkspaceVersionDto dto);
    Task<IResult> RemoveMemberAsync(int workspaceId, int userId, WorkspaceVersionDto dto);
    Task<IResult> ChangeMemberRoleAsync(int workspaceId, int userId, ChangeWorkspaceMemberRoleDto dto);
}
