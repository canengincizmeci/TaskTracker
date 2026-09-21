using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Bussiness.Concrete;

public class TaskShareManager(IUnitOfWork unitOfWork, ITaskShareDal taskShareDal,
    ICurrentUserService currentUserService, IEmailService emailService,
    INotificationService notificationService, IConfiguration configuration,
    ILogger<TaskShareManager> logger, ITaskActivityWriter activityWriter,
    ITaskWorkspaceService workspaceService) : ITaskShareService
{
    private const string TaskUnavailable = "This task is inactive or completed/cancelled and cannot receive participants.";
    private const string ConcurrentChange = "The task or invitation changed. Refresh and retry.";

    private static bool CanJoin(TaskRequest? task) => task is { Activity: true } &&
        task.Status is not TaskStatus.Completed and not TaskStatus.Cancelled;
    private static bool IsExpired(TaskShareInvitation invitation, DateTime now) =>
        invitation.ExpiresAt.HasValue && invitation.ExpiresAt.Value <= now;
    // Null represents invalid historical data. Never normalize it to a valid permission.
    private static string? PermissionName(TaskPermission permission) =>
        Enum.IsDefined(permission) ? permission.ToString() : null;

    public Task<IResult> AcceptTaskInvitationAsync(int invitationId) => RespondAsync(invitationId, true);
    public Task<IResult> RejectTaskInvitationAsync(int invitationId) => RespondAsync(invitationId, false);

    private async Task<IResult> RespondAsync(int invitationId, bool accept)
    {
        var invitation = await unitOfWork.GetRepository<TaskShareInvitation>().GetByIdAsync(invitationId);
        if (invitation is null) return new ErrorResult(Messages.InvitationNotFound);
        if (invitation.InvitedUserId != currentUserService.UserId) return new ErrorResult(Messages.AuthorizationDenied);
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(invitation.TaskRequestId);
        if (task is null) return new ErrorResult(Messages.DataNotFound);
        // Read the decision AFTER the task version: a response committed between these reads
        // must either be observed here or invalidate our subsequent version check.
        await taskShareDal.ReloadInvitationAsync(invitation);
        var target = accept ? TaskShareInvitationStatus.Accepted : TaskShareInvitationStatus.Rejected;
        var message = accept ? Messages.TaskAccepted : Messages.TaskRejected;
        if (invitation.Status == target) return new SuccessResult(message);
        if (invitation.Status != TaskShareInvitationStatus.Pending) return new ErrorResult(Messages.InvitationAlreadyResponded);
        var now = DateTime.UtcNow;
        if (IsExpired(invitation, now)) return new ErrorResult(Messages.InvitationExpired);
        if (accept)
        {
            if (!CanJoin(task)) return new ErrorResult(TaskUnavailable);
            if (!Enum.IsDefined(invitation.Permission)) return new ErrorResult(Messages.InvalidTaskPermission);
            if (await taskShareDal.GetAsync(x => x.TaskRequestId == task.Id && x.SharedWithUserId == invitation.InvitedUserId) is not null)
                return new ErrorResult(Messages.TaskAlreadyShared);
            await taskShareDal.AddAsync(new TaskShare
            {
                TaskRequestId = task.Id, SharedWithUserId = invitation.InvitedUserId,
                Permission = invitation.Permission, SharedAt = now
            });
        }
        invitation.Status = target;
        invitation.RespondedAt = now;
        var activity = await activityWriter.WriteAsync(task, currentUserService.UserId,
            accept ? TaskActivityType.InvitationAccepted : TaskActivityType.InvitationRejected,
            invitation: invitation);
        var result = await SaveInvitationChange(task, message);
        if (result.Success)
        {
            await workspaceService.PublishActivityAsync(activity);
            await workspaceService.PublishTaskChangedAsync(task.Id);
            if (task.OwnerId != currentUserService.UserId)
                await TryTaskNotification(task.OwnerId, accept ? "Invitation accepted" : "Invitation rejected",
                    $"A collaborator {(accept ? "accepted" : "rejected")} the invitation to '{task.Title}'.", task.Id);
        }
        return result;
    }

    private async Task<IResult> SaveInvitationChange(TaskRequest task, string message)
    {
        taskShareDal.TouchTask(task);
        try
        {
            // One transaction persists membership, invitation and version. Competing writes roll back.
            await unitOfWork.SaveChangesAsync();
            return new SuccessResult(message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ErrorResult(ConcurrentChange);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Invitation change could not be saved for task {TaskId}", task.Id);
            return new ErrorResult("The invitation change could not be saved. Refresh and retry.");
        }
    }

    public async Task<IDataResult<List<TaskInvitationDto>>> GetMyPendingInvitationsAsync()
    {
        var now = DateTime.UtcNow;
        var invitations = await unitOfWork.GetRepository<TaskShareInvitation>().GetAllAsync(x =>
            x.InvitedUserId == currentUserService.UserId && x.Status == TaskShareInvitationStatus.Pending &&
            (!x.ExpiresAt.HasValue || x.ExpiresAt > now) && x.TaskRequest.Activity &&
            x.TaskRequest.Status != TaskStatus.Completed && x.TaskRequest.Status != TaskStatus.Cancelled,
            include: q => q.Include(x => x.TaskRequest).Include(x => x.InvitedByUser));
        return new SuccessDataResult<List<TaskInvitationDto>>(invitations.OrderByDescending(x => x.CreatedAt)
            .Select(x => MapInvitation(x, now)).ToList());
    }

    public async Task<IDataResult<TaskInvitationDto>> GetInvitationAsync(int invitationId)
    {
        var invitation = await unitOfWork.GetRepository<TaskShareInvitation>().GetAsync(x =>
            x.Id == invitationId && x.InvitedUserId == currentUserService.UserId,
            include: q => q.Include(x => x.TaskRequest).Include(x => x.InvitedByUser));
        return invitation is null
            ? new ErrorDataResult<TaskInvitationDto>(Messages.InvitationNotFound)
            : new SuccessDataResult<TaskInvitationDto>(MapInvitation(invitation, DateTime.UtcNow));
    }

    private static TaskInvitationDto MapInvitation(TaskShareInvitation invitation, DateTime now)
    {
        var pending = invitation.Status == TaskShareInvitationStatus.Pending;
        var expired = pending && IsExpired(invitation, now);
        var available = pending && !expired && CanJoin(invitation.TaskRequest);
        var permission = PermissionName(invitation.Permission);
        return new TaskInvitationDto
        {
            Id = invitation.Id, TaskRequestId = invitation.TaskRequestId,
            TaskTitle = invitation.TaskRequest.Title, InviterUserName = invitation.InvitedByUser.UserName,
            Permission = permission, CreatedAt = invitation.CreatedAt, ExpiresAt = invitation.ExpiresAt,
            Status = expired ? "Expired" : invitation.Status.ToString(),
            CanAccept = available && permission is not null, CanReject = pending && !expired,
            UnavailableReason = !pending ? Messages.InvitationAlreadyResponded : expired ? Messages.InvitationExpired :
                !CanJoin(invitation.TaskRequest) ? TaskUnavailable : permission is null ? Messages.InvalidTaskPermission : null
        };
    }

    public async Task<IDataResult<List<TaskParticipantDto>>> GetParticipantsAsync(int taskId)
    {
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(taskId);
        if (task is null || !task.Activity) return new ErrorDataResult<List<TaskParticipantDto>>(Messages.DataNotFound);
        if (task.OwnerId != currentUserService.UserId &&
            !await taskShareDal.HasPermissionAsync(taskId, currentUserService.UserId, TaskPermission.View))
            return new ErrorDataResult<List<TaskParticipantDto>>(Messages.AuthorizationDenied);
        var shares = await taskShareDal.GetAllAsync(x => x.TaskRequestId == taskId,
            include: q => q.Include(x => x.SharedWithUser));
        return new SuccessDataResult<List<TaskParticipantDto>>(shares.OrderBy(x => x.SharedAt).ThenBy(x => x.Id)
            .Select(x => new TaskParticipantDto
            {
                UserId = x.SharedWithUserId, UserName = x.SharedWithUser.UserName,
                Permission = PermissionName(x.Permission), SharedAt = x.SharedAt,
                IsAssignee = task.AssigneeUserId == x.SharedWithUserId
            }).ToList());
    }

    public async Task<IDataResult<List<SharedTaskDto>>> GetMySharedTasksAsync()
    {
        var shares = await taskShareDal.GetAllAsync(x => x.SharedWithUserId == currentUserService.UserId && x.TaskRequest.Activity &&
            x.Permission >= TaskPermission.View && x.Permission <= TaskPermission.Manage,
            include: q => q.Include(x => x.TaskRequest).ThenInclude(x => x.Owner)
                .Include(x => x.TaskRequest).ThenInclude(x => x.Assignee));
        return new SuccessDataResult<List<SharedTaskDto>>(shares.Select(MapShare).ToList(), Messages.DataListed);
    }

    private static SharedTaskDto MapShare(TaskShare share) => new()
    {
        TaskId = share.TaskRequestId, Title = share.TaskRequest.Title, Category = share.TaskRequest.Category,
        Priority = share.TaskRequest.Priority.ToString(), Status = share.TaskRequest.Status.ToString(),
        DueDate = share.TaskRequest.DueDate, OwnerId = share.TaskRequest.OwnerId,
        OwnerUserName = share.TaskRequest.Owner.UserName, AssigneeUserId = share.TaskRequest.AssigneeUserId,
        AssigneeUserName = share.TaskRequest.Assignee?.UserName,
        Permission = PermissionName(share.Permission), SharedAt = share.SharedAt
    };

    public async Task<IDataResult<SharedTaskDto>> GetSharedTaskDetailsAsync(int taskShareId, int currentUserId)
    {
        var share = await taskShareDal.GetSharedTaskDetailsAsync(taskShareId);
        if (share is null || !share.TaskRequest.Activity) return new ErrorDataResult<SharedTaskDto>(Messages.DataNotFound);
        if (share.TaskRequest.OwnerId != currentUserId &&
            (share.SharedWithUserId != currentUserId || !Enum.IsDefined(share.Permission)))
            return new ErrorDataResult<SharedTaskDto>(Messages.AuthorizationDenied);
        return new SuccessDataResult<SharedTaskDto>(MapShare(share), Messages.DataListed);
    }

    public async Task<IResult> InviteUserToTask(InviteUserToTaskDto dto)
    {
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(dto.TaskRequestId);
        if (task is null) return new ErrorResult(Messages.DataNotFound);
        if (task.OwnerId != currentUserService.UserId) return new ErrorResult(Messages.AuthorizationDenied);
        if (!CanJoin(task)) return new ErrorResult(TaskUnavailable);
        if (!Enum.IsDefined(dto.Permission)) return new ErrorResult(Messages.InvalidTaskPermission);
        if (string.IsNullOrWhiteSpace(dto.Username)) return new ErrorResult("Username is required.");
        var users = unitOfWork.GetRepository<User>();
        var user = await users.GetAsync(x => x.UserName == dto.Username.Trim());
        if (user is null) return new ErrorResult(Messages.UserNotFound);
        if (user.Id == task.OwnerId) return new ErrorResult(Messages.UserCannotShareTaskWithSelf);
        if (await taskShareDal.GetAsync(x => x.TaskRequestId == task.Id && x.SharedWithUserId == user.Id) is not null)
            return new ErrorResult(Messages.TaskAlreadyShared);
        var invitations = unitOfWork.GetRepository<TaskShareInvitation>();
        var now = DateTime.UtcNow;
        if (await invitations.GetAsync(x => x.TaskRequestId == task.Id && x.InvitedUserId == user.Id &&
            x.Status == TaskShareInvitationStatus.Pending && (!x.ExpiresAt.HasValue || x.ExpiresAt > now)) is not null)
            return new ErrorResult(Messages.TaskShareInvitationAlreadySent);
        var inviter = await users.GetByIdAsync(currentUserService.UserId);
        if (inviter is null) return new ErrorResult(Messages.UserNotFound);
        var invitation = new TaskShareInvitation
        {
            TaskRequestId = task.Id, InvitedUserId = user.Id, InvitedByUserId = inviter.Id,
            Permission = dto.Permission, Status = TaskShareInvitationStatus.Pending,
            CreatedAt = now, ExpiresAt = now.AddDays(7)
        };
        await invitations.AddAsync(invitation);
        var activity = await activityWriter.WriteAsync(task, currentUserService.UserId,
            TaskActivityType.UserInvited, user.Id, invitation);
        var result = await SaveInvitationChange(task, Messages.TaskShareInvitationSent);
        if (!result.Success) return result;
        await workspaceService.PublishActivityAsync(activity);
        // Delivery is best effort after commit; it cannot turn a persisted invitation into a failed request.
        try
        {
            await notificationService.CreateTaskShareInvitationNotificationAsync(user.Id, task.Title, inviter.UserName, invitation.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notification delivery failed for persisted invitation {InvitationId}", invitation.Id);
        }
        try
        {
            var clientUrl = configuration["ClientApp:BaseUrl"];
            if (string.IsNullOrWhiteSpace(clientUrl)) clientUrl = "http://localhost:5173";
            await emailService.SendTaskShareInvitationEmailAsync(user.Email, task.Title, inviter.UserName,
                $"{clientUrl.TrimEnd('/')}/tasks/invitations/{invitation.Id}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Email delivery failed for persisted invitation {InvitationId}", invitation.Id);
        }
        return result;
    }

    public async Task<IDataResult<List<OutgoingTaskInvitationDto>>> GetOutgoingInvitationsAsync(int taskId)
    {
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(taskId);
        if (task is null || !task.Activity) return new ErrorDataResult<List<OutgoingTaskInvitationDto>>(Messages.DataNotFound);
        if (task.OwnerId != currentUserService.UserId) return new ErrorDataResult<List<OutgoingTaskInvitationDto>>(Messages.AuthorizationDenied);
        var invitations = await unitOfWork.GetRepository<TaskShareInvitation>().GetAllAsync(x =>
            x.TaskRequestId == taskId && x.Status == TaskShareInvitationStatus.Pending,
            q => q.Include(x => x.InvitedUser));
        var now = DateTime.UtcNow;
        return new SuccessDataResult<List<OutgoingTaskInvitationDto>>(invitations.OrderByDescending(x => x.CreatedAt)
            .Select(x => new OutgoingTaskInvitationDto
            {
                Id = x.Id, TaskRequestId = x.TaskRequestId, InvitedUserName = x.InvitedUser.UserName,
                Permission = PermissionName(x.Permission), Status = IsExpired(x, now) ? "Expired" : x.Status.ToString(),
                CreatedAt = x.CreatedAt, ExpiresAt = x.ExpiresAt,
                CanCancel = !IsExpired(x, now) && x.Status == TaskShareInvitationStatus.Pending
            }).ToList());
    }

    public async Task<IResult> CancelInvitationAsync(int invitationId, long version)
    {
        var invitation = await unitOfWork.GetRepository<TaskShareInvitation>().GetByIdAsync(invitationId);
        if (invitation is null) return new ErrorResult(Messages.InvitationNotFound);
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(invitation.TaskRequestId);
        if (task is null || !task.Activity) return new ErrorResult(Messages.DataNotFound);
        if (task.OwnerId != currentUserService.UserId) return new ErrorResult(Messages.AuthorizationDenied);
        if (task.Version != version) return new ErrorResult(ConcurrentChange);
        await taskShareDal.ReloadInvitationAsync(invitation);
        if (invitation.Status != TaskShareInvitationStatus.Pending)
            return new ErrorResult("Only a pending invitation can be cancelled.");
        invitation.Status = TaskShareInvitationStatus.Cancelled;
        invitation.RespondedAt = DateTime.UtcNow;
        var activity = await activityWriter.WriteAsync(task, currentUserService.UserId,
            TaskActivityType.InvitationCancelled, invitation.InvitedUserId, invitation);
        var result = await SaveInvitationChange(task, "Invitation cancelled.");
        if (result.Success)
        {
            await workspaceService.PublishActivityAsync(activity);
            await workspaceService.PublishTaskChangedAsync(task.Id);
        }
        return result;
    }

    public async Task<IResult> UpdateParticipantPermissionAsync(int taskId, int userId, UpdateParticipantPermissionDto dto)
    {
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(taskId);
        if (task is null || !task.Activity) return new ErrorResult(Messages.DataNotFound);
        if (task.OwnerId != currentUserService.UserId) return new ErrorResult(Messages.AuthorizationDenied);
        if (userId == task.OwnerId) return new ErrorResult("The owner is not a removable participant.");
        if (task.Version != dto.Version) return new ErrorResult(ConcurrentChange);
        if (!Enum.IsDefined(dto.Permission)) return new ErrorResult(Messages.InvalidTaskPermission);
        var share = await taskShareDal.GetAsync(x => x.TaskRequestId == taskId && x.SharedWithUserId == userId);
        if (share is null) return new ErrorResult("Participant not found.");
        if (share.Permission == dto.Permission) return new SuccessResult("Permission is already set.");
        share.Permission = dto.Permission;
        TaskActivity? unassigned = null;
        if (task.AssigneeUserId == userId && dto.Permission < TaskPermission.Edit)
        {
            task.AssigneeUserId = null;
            if (task.Status == TaskStatus.InProgress) task.Status = TaskStatus.Pending;
            unassigned = await activityWriter.WriteAsync(task, currentUserService.UserId, TaskActivityType.UserUnassigned, userId);
        }
        var activity = await activityWriter.WriteAsync(task, currentUserService.UserId,
            TaskActivityType.ParticipantPermissionChanged, userId);
        var result = await SaveInvitationChange(task, "Participant permission updated.");
        if (!result.Success) return result;
        if (unassigned is not null) await workspaceService.PublishActivityAsync(unassigned);
        await workspaceService.PublishActivityAsync(activity);
        await workspaceService.PublishTaskChangedAsync(task.Id);
        await TryTaskNotification(userId, "Task access changed",
            $"Your access to '{task.Title}' is now {dto.Permission}.", task.Id);
        return result;
    }

    public async Task<IResult> RemoveParticipantAsync(int taskId, int userId, long version)
    {
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(taskId);
        if (task is null || !task.Activity) return new ErrorResult(Messages.DataNotFound);
        if (task.OwnerId != currentUserService.UserId) return new ErrorResult(Messages.AuthorizationDenied);
        if (userId == task.OwnerId) return new ErrorResult("The task owner cannot be removed.");
        if (task.Version != version) return new ErrorResult(ConcurrentChange);
        if (task.Status == TaskStatus.InReview) return new ErrorResult("Participants cannot be removed while a task is under review.");
        var share = await taskShareDal.GetAsync(x => x.TaskRequestId == taskId && x.SharedWithUserId == userId);
        if (share is null) return new ErrorResult("Participant not found.");
        TaskActivity? unassigned = null;
        if (task.AssigneeUserId == userId)
        {
            task.AssigneeUserId = null;
            if (task.Status == TaskStatus.InProgress) task.Status = TaskStatus.Pending;
            unassigned = await activityWriter.WriteAsync(task, currentUserService.UserId, TaskActivityType.UserUnassigned, userId);
        }
        taskShareDal.Delete(share);
        var activity = await activityWriter.WriteAsync(task, currentUserService.UserId,
            TaskActivityType.ParticipantRemoved, userId);
        var result = await SaveInvitationChange(task, "Participant removed.");
        if (!result.Success) return result;
        if (unassigned is not null) await workspaceService.PublishActivityAsync(unassigned);
        await workspaceService.PublishActivityAsync(activity);
        await workspaceService.PublishTaskChangedAsync(task.Id);
        try { await workspaceService.RevokeAccessAsync(task.Id, userId); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not revoke live workspace access for task {TaskId}", task.Id); }
        await TryTaskNotification(userId, "Removed from task", $"You no longer have access to '{task.Title}'.",
            task.Id, "/tasks/shared-tasks");
        return result;
    }

    private async Task TryTaskNotification(int userId, string title, string message, int taskId,
        string? redirectUrl = null)
    {
        try
        {
            await notificationService.CreateTaskNotificationAsync(userId, NotificationType.TaskAccessChanged,
                title, message, taskId, redirectUrl);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Task notification delivery failed for task {TaskId}", taskId); }
    }
}
