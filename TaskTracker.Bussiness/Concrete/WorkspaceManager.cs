using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Concrete;

public class WorkspaceManager(IUnitOfWork unitOfWork, IWorkspaceDal workspaceDal,
    ICurrentUserService currentUserService, ILogger<WorkspaceManager> logger) : IWorkspaceService
{
    private static bool CanManage(WorkspaceMember membership) =>
        membership.Role is WorkspaceRole.Owner or WorkspaceRole.Admin;

    private static bool IsExpired(WorkspaceInvitation invitation, DateTime now) =>
        invitation.ExpiresAt.HasValue && invitation.ExpiresAt.Value <= now;

    public async Task<IDataResult<WorkspaceDetailDto>> CreateAsync(CreateWorkspaceDto dto)
    {
        var name = NormalizeName(dto.Name);
        if (name is null) return new ErrorDataResult<WorkspaceDetailDto>(WorkspaceMessages.NameRequired);
        if (await unitOfWork.GetRepository<User>().GetByIdAsync(currentUserService.UserId) is null)
            return new ErrorDataResult<WorkspaceDetailDto>(Messages.UserNotFound);

        var now = DateTime.UtcNow;
        var workspace = new Workspace
        {
            Name = name,
            CreatedAt = now,
            Members = [new WorkspaceMember
            {
                UserId = currentUserService.UserId,
                Role = WorkspaceRole.Owner,
                IsActive = true,
                JoinedAt = now
            }],
            Activities = [new WorkspaceActivity
            {
                ActorUserId = currentUserService.UserId,
                ActivityType = WorkspaceActivityType.WorkspaceCreated,
                CreatedAt = now
            }]
        };
        await unitOfWork.GetRepository<Workspace>().AddAsync(workspace);
        var saved = await SaveAsync(WorkspaceMessages.Created, workspace.Id);
        if (!saved.Success) return DataFailure<WorkspaceDetailDto>(saved);

        var detail = await workspaceDal.GetDetailsAsync(workspace.Id, currentUserService.UserId);
        return detail is null
            ? new ErrorDataResult<WorkspaceDetailDto>(WorkspaceMessages.NotFound)
            : new SuccessDataResult<WorkspaceDetailDto>(detail, WorkspaceMessages.Created);
    }

    public async Task<IDataResult<List<WorkspaceListItemDto>>> GetMineAsync() =>
        new SuccessDataResult<List<WorkspaceListItemDto>>(
            await workspaceDal.GetForUserAsync(currentUserService.UserId));

    public async Task<IDataResult<WorkspaceDetailDto>> GetDetailsAsync(int workspaceId)
    {
        var detail = await workspaceDal.GetDetailsAsync(workspaceId, currentUserService.UserId);
        return detail is null
            ? new ErrorDataResult<WorkspaceDetailDto>(WorkspaceMessages.NotFound)
            : new SuccessDataResult<WorkspaceDetailDto>(detail);
    }

    public async Task<IDataResult<List<WorkspaceMemberDto>>> GetMembersAsync(int workspaceId)
    {
        if (await workspaceDal.GetActiveMembershipAsync(workspaceId, currentUserService.UserId) is null)
            return new ErrorDataResult<List<WorkspaceMemberDto>>(WorkspaceMessages.NotFound);
        return new SuccessDataResult<List<WorkspaceMemberDto>>(await workspaceDal.GetMembersAsync(workspaceId));
    }

    public async Task<IResult> RenameAsync(int workspaceId, RenameWorkspaceDto dto)
    {
        var actor = await workspaceDal.GetActiveMembershipAsync(workspaceId, currentUserService.UserId);
        if (actor is null) return new ErrorResult(WorkspaceMessages.NotFound);
        if (actor.Role != WorkspaceRole.Owner) return new ErrorResult(Messages.AuthorizationDenied);
        var workspace = await workspaceDal.GetWorkspaceAsync(workspaceId);
        if (workspace is null) return new ErrorResult(WorkspaceMessages.NotFound);
        if (workspace.Version != dto.Version) return new ConflictResult(WorkspaceMessages.ConcurrentChange);
        var name = NormalizeName(dto.Name);
        if (name is null) return new ErrorResult(WorkspaceMessages.NameRequired);
        if (workspace.Name == name) return new SuccessResult(WorkspaceMessages.Renamed);

        workspace.Name = name;
        workspaceDal.TouchMembership(actor);
        await AddActivityAsync(workspaceId, WorkspaceActivityType.WorkspaceRenamed);
        return await SaveAsync(WorkspaceMessages.Renamed, workspaceId);
    }

    public async Task<IDataResult<WorkspaceInvitationDto>> InviteAsync(int workspaceId,
        CreateWorkspaceInvitationDto dto)
    {
        var actor = await workspaceDal.GetActiveMembershipAsync(workspaceId, currentUserService.UserId);
        if (actor is null) return new ErrorDataResult<WorkspaceInvitationDto>(WorkspaceMessages.NotFound);
        if (!CanManage(actor)) return new ErrorDataResult<WorkspaceInvitationDto>(Messages.AuthorizationDenied);
        if (string.IsNullOrWhiteSpace(dto.Username))
            return new ErrorDataResult<WorkspaceInvitationDto>(WorkspaceMessages.UsernameRequired);
        var invitedUser = await workspaceDal.GetUserByUsernameAsync(dto.Username.Trim());
        if (invitedUser is null) return new ErrorDataResult<WorkspaceInvitationDto>(Messages.UserNotFound);
        if (invitedUser.Id == currentUserService.UserId)
            return new ErrorDataResult<WorkspaceInvitationDto>(WorkspaceMessages.CannotInviteSelf);
        if (await workspaceDal.GetActiveMembershipAsync(workspaceId, invitedUser.Id) is not null)
            return new ConflictDataResult<WorkspaceInvitationDto>(WorkspaceMessages.AlreadyMember);

        var now = DateTime.UtcNow;
        var pending = await workspaceDal.GetPendingInvitationAsync(workspaceId, invitedUser.Id);
        if (pending is not null)
        {
            if (!IsExpired(pending, now))
                return new ConflictDataResult<WorkspaceInvitationDto>(WorkspaceMessages.PendingInvitationExists);
            pending.Status = WorkspaceInvitationStatus.Expired;
            pending.RespondedAt = now;
        }

        var invitation = new WorkspaceInvitation
        {
            WorkspaceId = workspaceId,
            InvitedUserId = invitedUser.Id,
            InvitedByUserId = currentUserService.UserId,
            Status = WorkspaceInvitationStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddDays(7)
        };
        await unitOfWork.GetRepository<WorkspaceInvitation>().AddAsync(invitation);
        workspaceDal.TouchMembership(actor);
        await AddActivityAsync(workspaceId, WorkspaceActivityType.UserInvited, invitedUser.Id);
        var saved = await SaveAsync(WorkspaceMessages.InvitationCreated, workspaceId);
        if (!saved.Success) return DataFailure<WorkspaceInvitationDto>(saved);

        var created = (await workspaceDal.GetInvitationsAsync(workspaceId)).Single(x => x.Id == invitation.Id);
        return new SuccessDataResult<WorkspaceInvitationDto>(created, WorkspaceMessages.InvitationCreated);
    }

    public async Task<IDataResult<List<WorkspaceInvitationDto>>> GetInvitationsAsync(int workspaceId)
    {
        var actor = await workspaceDal.GetActiveMembershipAsync(workspaceId, currentUserService.UserId);
        if (actor is null) return new ErrorDataResult<List<WorkspaceInvitationDto>>(WorkspaceMessages.NotFound);
        if (!CanManage(actor)) return new ErrorDataResult<List<WorkspaceInvitationDto>>(Messages.AuthorizationDenied);
        var invitations = await workspaceDal.GetInvitationsAsync(workspaceId);
        MarkExpiredForDisplay(invitations, DateTime.UtcNow);
        return new SuccessDataResult<List<WorkspaceInvitationDto>>(invitations);
    }

    public async Task<IDataResult<List<WorkspaceInvitationDto>>> GetMyInvitationsAsync() =>
        new SuccessDataResult<List<WorkspaceInvitationDto>>(
            await workspaceDal.GetPendingInvitationsForUserAsync(currentUserService.UserId, DateTime.UtcNow));

    public Task<IResult> AcceptInvitationAsync(int invitationId, WorkspaceVersionDto dto) =>
        RespondToInvitationAsync(invitationId, dto.Version, accept: true);

    public Task<IResult> RejectInvitationAsync(int invitationId, WorkspaceVersionDto dto) =>
        RespondToInvitationAsync(invitationId, dto.Version, accept: false);

    private async Task<IResult> RespondToInvitationAsync(int invitationId, long version, bool accept)
    {
        var invitation = await workspaceDal.GetInvitationAsync(invitationId);
        if (invitation is null || invitation.InvitedUserId != currentUserService.UserId)
            return new ErrorResult(WorkspaceMessages.InvitationNotFound);
        var targetStatus = accept ? WorkspaceInvitationStatus.Accepted : WorkspaceInvitationStatus.Rejected;
        var successMessage = accept ? WorkspaceMessages.InvitationAccepted : WorkspaceMessages.InvitationRejected;
        if (invitation.Status == targetStatus) return new SuccessResult(successMessage);
        if (invitation.Version != version) return new ConflictResult(WorkspaceMessages.ConcurrentChange);
        if (invitation.Status != WorkspaceInvitationStatus.Pending)
            return new ConflictResult(WorkspaceMessages.InvitationAlreadyResponded);

        var now = DateTime.UtcNow;
        if (IsExpired(invitation, now))
            return await ExpireInvitationAsync(invitation, now);

        if (accept)
        {
            var membership = await workspaceDal.GetMembershipAsync(invitation.WorkspaceId, currentUserService.UserId);
            if (membership is { IsActive: true })
                return new ConflictResult(WorkspaceMessages.AlreadyMember);
            if (membership is null)
            {
                membership = new WorkspaceMember
                {
                    WorkspaceId = invitation.WorkspaceId,
                    UserId = currentUserService.UserId,
                    Role = WorkspaceRole.Member,
                    IsActive = true,
                    JoinedAt = now
                };
                await unitOfWork.GetRepository<WorkspaceMember>().AddAsync(membership);
            }
            else
            {
                membership.Role = WorkspaceRole.Member;
                membership.IsActive = true;
                membership.RemovedAt = null;
                membership.JoinedAt = now;
                await AddActivityAsync(invitation.WorkspaceId, WorkspaceActivityType.MemberReactivated,
                    currentUserService.UserId);
            }
        }

        invitation.Status = targetStatus;
        invitation.RespondedAt = now;
        await AddActivityAsync(invitation.WorkspaceId,
            accept ? WorkspaceActivityType.InvitationAccepted : WorkspaceActivityType.InvitationRejected,
            currentUserService.UserId);
        return await SaveAsync(successMessage, invitation.WorkspaceId);
    }

    public async Task<IResult> CancelInvitationAsync(int workspaceId, int invitationId, WorkspaceVersionDto dto)
    {
        var actor = await workspaceDal.GetActiveMembershipAsync(workspaceId, currentUserService.UserId);
        if (actor is null) return new ErrorResult(WorkspaceMessages.NotFound);
        if (!CanManage(actor)) return new ErrorResult(Messages.AuthorizationDenied);
        var invitation = await workspaceDal.GetInvitationAsync(invitationId);
        if (invitation is null || invitation.WorkspaceId != workspaceId)
            return new ErrorResult(WorkspaceMessages.InvitationNotFound);
        if (invitation.Status == WorkspaceInvitationStatus.Cancelled)
            return new SuccessResult(WorkspaceMessages.InvitationCancelled);
        if (invitation.Version != dto.Version) return new ConflictResult(WorkspaceMessages.ConcurrentChange);
        if (invitation.Status != WorkspaceInvitationStatus.Pending)
            return new ConflictResult(WorkspaceMessages.InvitationAlreadyResponded);
        var now = DateTime.UtcNow;
        if (IsExpired(invitation, now)) return await ExpireInvitationAsync(invitation, now);

        invitation.Status = WorkspaceInvitationStatus.Cancelled;
        invitation.RespondedAt = now;
        workspaceDal.TouchMembership(actor);
        await AddActivityAsync(workspaceId, WorkspaceActivityType.InvitationCancelled, invitation.InvitedUserId);
        return await SaveAsync(WorkspaceMessages.InvitationCancelled, workspaceId);
    }

    public async Task<IResult> RemoveMemberAsync(int workspaceId, int userId, WorkspaceVersionDto dto)
    {
        var actor = await workspaceDal.GetActiveMembershipAsync(workspaceId, currentUserService.UserId);
        if (actor is null) return new ErrorResult(WorkspaceMessages.NotFound);
        if (!CanManage(actor)) return new ErrorResult(Messages.AuthorizationDenied);
        var target = await workspaceDal.GetMembershipAsync(workspaceId, userId);
        if (target is null) return new ErrorResult(WorkspaceMessages.MemberNotFound);
        if (!target.IsActive) return new ConflictResult(WorkspaceMessages.MembershipInactive);
        if (target.Role == WorkspaceRole.Owner)
            return actor.UserId == target.UserId
                ? new ErrorResult(WorkspaceMessages.OwnerCannotBeRemoved)
                : new ErrorResult(Messages.AuthorizationDenied);
        if (actor.Role == WorkspaceRole.Admin && target.Role != WorkspaceRole.Member)
            return new ErrorResult(Messages.AuthorizationDenied);
        if (target.Version != dto.Version) return new ConflictResult(WorkspaceMessages.ConcurrentChange);

        target.IsActive = false;
        target.RemovedAt = DateTime.UtcNow;
        workspaceDal.TouchMembership(actor);
        await AddActivityAsync(workspaceId, WorkspaceActivityType.MemberRemoved, userId);
        return await SaveAsync(WorkspaceMessages.MemberRemoved, workspaceId);
    }

    public async Task<IResult> ChangeMemberRoleAsync(int workspaceId, int userId,
        ChangeWorkspaceMemberRoleDto dto)
    {
        var actor = await workspaceDal.GetActiveMembershipAsync(workspaceId, currentUserService.UserId);
        if (actor is null) return new ErrorResult(WorkspaceMessages.NotFound);
        if (actor.Role != WorkspaceRole.Owner) return new ErrorResult(Messages.AuthorizationDenied);
        var target = await workspaceDal.GetMembershipAsync(workspaceId, userId);
        if (target is null || !target.IsActive) return new ErrorResult(WorkspaceMessages.MemberNotFound);
        if (target.Role == WorkspaceRole.Owner) return new ErrorResult(WorkspaceMessages.OwnerCannotChangeOwnRole);
        var valid = target.Role == WorkspaceRole.Member && dto.Role == WorkspaceRole.Admin ||
                    target.Role == WorkspaceRole.Admin && dto.Role == WorkspaceRole.Member;
        if (!valid) return new ErrorResult(WorkspaceMessages.InvalidRoleChange);
        if (target.Version != dto.Version) return new ConflictResult(WorkspaceMessages.ConcurrentChange);

        var previous = target.Role;
        target.Role = dto.Role;
        workspaceDal.TouchMembership(actor);
        await AddActivityAsync(workspaceId,
            dto.Role == WorkspaceRole.Admin ? WorkspaceActivityType.RolePromoted : WorkspaceActivityType.RoleDemoted,
            userId, previous, dto.Role);
        return await SaveAsync(WorkspaceMessages.RoleChanged, workspaceId);
    }

    private async Task<IResult> ExpireInvitationAsync(WorkspaceInvitation invitation, DateTime now)
    {
        invitation.Status = WorkspaceInvitationStatus.Expired;
        invitation.RespondedAt = now;
        var saved = await SaveAsync(WorkspaceMessages.InvitationExpired, invitation.WorkspaceId);
        return saved.Success ? new ConflictResult(WorkspaceMessages.InvitationExpired) : saved;
    }

    private Task AddActivityAsync(int workspaceId, WorkspaceActivityType type, int? targetUserId = null,
        WorkspaceRole? fromRole = null, WorkspaceRole? toRole = null) =>
        unitOfWork.GetRepository<WorkspaceActivity>().AddAsync(new WorkspaceActivity
        {
            WorkspaceId = workspaceId,
            ActorUserId = currentUserService.UserId,
            TargetUserId = targetUserId,
            ActivityType = type,
            FromRole = fromRole,
            ToRole = toRole,
            CreatedAt = DateTime.UtcNow
        });

    private async Task<IResult> SaveAsync(string successMessage, int workspaceId)
    {
        try
        {
            await unitOfWork.SaveChangesAsync();
            return new SuccessResult(successMessage);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ConflictResult(WorkspaceMessages.ConcurrentChange);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Workspace change could not be saved for workspace {WorkspaceId}", workspaceId);
            return new ConflictResult(WorkspaceMessages.ConflictingState);
        }
        catch (ValidationException ex)
        {
            return new ErrorResult(ex.Message);
        }
    }

    private static IDataResult<T> DataFailure<T>(IResult result) => result is IConflictResult
        ? new ConflictDataResult<T>(result.Message)
        : new ErrorDataResult<T>(result.Message);

    private static string? NormalizeName(string? name)
    {
        var normalized = name?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > Workspace.MaxNameLength
            ? null
            : normalized;
    }

    private static void MarkExpiredForDisplay(IEnumerable<WorkspaceInvitationDto> invitations, DateTime now)
    {
        foreach (var invitation in invitations.Where(x => x.Status == WorkspaceInvitationStatus.Pending &&
                     x.ExpiresAt.HasValue && x.ExpiresAt.Value <= now))
            invitation.Status = WorkspaceInvitationStatus.Expired;
    }
}
