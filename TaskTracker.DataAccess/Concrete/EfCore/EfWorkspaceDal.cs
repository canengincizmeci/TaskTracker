using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.DataAccess.Concrete.EfCore;

public class EfWorkspaceDal(TaskTrackerDbContext context) : IWorkspaceDal
{
    public Task<List<WorkspaceListItemDto>> GetForUserAsync(int userId) =>
        context.WorkspaceMembers.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .OrderBy(x => x.Workspace.Name).ThenBy(x => x.WorkspaceId)
            .Select(x => new WorkspaceListItemDto
            {
                Id = x.WorkspaceId,
                Name = x.Workspace.Name,
                Role = x.Role,
                CreatedAt = x.Workspace.CreatedAt
            }).ToListAsync();

    public Task<WorkspaceDetailDto?> GetDetailsAsync(int workspaceId, int userId) =>
        context.Workspaces.AsNoTracking()
            .Where(x => x.Id == workspaceId && x.Members.Any(m => m.UserId == userId && m.IsActive))
            .Select(x => new WorkspaceDetailDto
            {
                Id = x.Id,
                Name = x.Name,
                CreatedAt = x.CreatedAt,
                Version = x.Version,
                CurrentUserRole = x.Members.Where(m => m.UserId == userId && m.IsActive).Select(m => m.Role).Single(),
                MemberCount = x.Members.Count(m => m.IsActive),
                Members = x.Members.Where(m => m.IsActive)
                    .OrderBy(m => m.JoinedAt).ThenBy(m => m.UserId)
                    .Select(m => new WorkspaceMemberDto
                    {
                        UserId = m.UserId,
                        UserName = m.User.UserName,
                        Role = m.Role,
                        JoinedAt = m.JoinedAt,
                        Version = m.Version
                    }).ToList()
            }).SingleOrDefaultAsync();

    public Task<List<WorkspaceMemberDto>> GetMembersAsync(int workspaceId) =>
        context.WorkspaceMembers.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.IsActive)
            .OrderBy(x => x.JoinedAt).ThenBy(x => x.UserId)
            .Select(x => new WorkspaceMemberDto
            {
                UserId = x.UserId,
                UserName = x.User.UserName,
                Role = x.Role,
                JoinedAt = x.JoinedAt,
                Version = x.Version
            }).ToListAsync();

    public Task<List<WorkspaceInvitationDto>> GetInvitationsAsync(int workspaceId) =>
        context.WorkspaceInvitations.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new WorkspaceInvitationDto
            {
                Id = x.Id,
                WorkspaceId = x.WorkspaceId,
                WorkspaceName = x.Workspace.Name,
                InvitedUserId = x.InvitedUserId,
                InvitedUserName = x.InvitedUser.UserName,
                InvitedByUserName = x.InvitedByUser.UserName,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                RespondedAt = x.RespondedAt,
                ExpiresAt = x.ExpiresAt,
                Version = x.Version
            }).ToListAsync();

    public Task<List<WorkspaceInvitationDto>> GetPendingInvitationsForUserAsync(int userId, DateTime now) =>
        context.WorkspaceInvitations.AsNoTracking()
            .Where(x => x.InvitedUserId == userId && x.Status == WorkspaceInvitationStatus.Pending &&
                        (!x.ExpiresAt.HasValue || x.ExpiresAt > now))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new WorkspaceInvitationDto
            {
                Id = x.Id,
                WorkspaceId = x.WorkspaceId,
                WorkspaceName = x.Workspace.Name,
                InvitedUserId = x.InvitedUserId,
                InvitedUserName = x.InvitedUser.UserName,
                InvitedByUserName = x.InvitedByUser.UserName,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                RespondedAt = x.RespondedAt,
                ExpiresAt = x.ExpiresAt,
                Version = x.Version
            }).ToListAsync();

    public Task<Workspace?> GetWorkspaceAsync(int workspaceId) =>
        context.Workspaces.SingleOrDefaultAsync(x => x.Id == workspaceId);

    public Task<WorkspaceMember?> GetActiveMembershipAsync(int workspaceId, int userId) =>
        context.WorkspaceMembers.SingleOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.UserId == userId && x.IsActive);

    public Task<WorkspaceMember?> GetMembershipAsync(int workspaceId, int userId) =>
        context.WorkspaceMembers.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId);

    public Task<WorkspaceInvitation?> GetInvitationAsync(int invitationId) =>
        context.WorkspaceInvitations.SingleOrDefaultAsync(x => x.Id == invitationId);

    public Task<WorkspaceInvitation?> GetPendingInvitationAsync(int workspaceId, int invitedUserId) =>
        context.WorkspaceInvitations.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId &&
            x.InvitedUserId == invitedUserId && x.Status == WorkspaceInvitationStatus.Pending);

    public Task<User?> GetUserByUsernameAsync(string username) =>
        context.Users.SingleOrDefaultAsync(x => x.UserName == username);

    public void TouchMembership(WorkspaceMember membership) =>
        context.Entry(membership).Property(x => x.Version).IsModified = true;
}
