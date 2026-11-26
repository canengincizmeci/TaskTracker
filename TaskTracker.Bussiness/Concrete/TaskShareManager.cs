using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Storage;
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
    ITaskCollaborationService collaborationService, IIdentityNormalizer identityNormalizer, ISubTaskDal subTaskDal)
    : ITaskShareService
{
    private const string TaskUnavailable = "This task is inactive or completed/cancelled and cannot receive participants.";
    private const string ConcurrentChange = "The task or invitation changed. Refresh and retry.";
    private const string WorkspaceTaskSharingUnavailable =
        "Workspace tasks use Workspace membership and cannot be shared directly.";

    private static bool CanJoin(TaskRequest? task) => task is { Activity: true } &&
        task.Status is not TaskStatus.Completed and not TaskStatus.Cancelled;
    private static bool IsExpired(TaskShareInvitation invitation, DateTime now) =>
        invitation.ExpiresAt.HasValue && invitation.ExpiresAt.Value <= now;
    // Null represents invalid historical data. Never normalize it to a valid permission.
    private static string? PermissionName(TaskPermission permission) =>
        Enum.IsDefined(permission) ? permission.ToString() : null;

    public Task<IResult> AcceptTaskInvitationAsync(int invitationId) => RespondAsync(invitationId, true, null);
    public Task<IResult> RejectTaskInvitationAsync(int invitationId) => RespondAsync(invitationId, false, null);
    public Task<IResult> RespondToInvitationAsync(int invitationId, bool accept, string? responseMessage) =>
        RespondAsync(invitationId, accept, responseMessage);

    private async Task<IResult> RespondAsync(int invitationId, bool accept, string? responseMessage)
    {
        responseMessage = responseMessage?.Trim();
        if (responseMessage?.Length > TaskShareInvitation.MaxMessageLength)
            return new ErrorResult($"A response may contain at most {TaskShareInvitation.MaxMessageLength} characters.");
        var invitation = await unitOfWork.GetRepository<TaskShareInvitation>().GetByIdAsync(invitationId);
        if (invitation is null) return new ErrorResult(Messages.InvitationNotFound);
        if (invitation.InvitedUserId != currentUserService.UserId) return new ErrorResult(Messages.AuthorizationDenied);
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(invitation.TaskRequestId);
        if (task is null) return new ErrorResult(Messages.DataNotFound);
        if (task.WorkspaceId.HasValue) return new ConflictResult(WorkspaceTaskSharingUnavailable);
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
        invitation.ResponseMessage = string.IsNullOrWhiteSpace(responseMessage) ? null : responseMessage;
        var activity = await activityWriter.WriteAsync(task, currentUserService.UserId,
            accept ? TaskActivityType.InvitationAccepted : TaskActivityType.InvitationRejected,
            invitation: invitation);
        var result = await SaveInvitationChange(task, message);
        if (result.Success)
        {
            await collaborationService.PublishActivityAsync(activity);
            await collaborationService.PublishTaskChangedAsync(task.Id);
            if (task.OwnerId != currentUserService.UserId)
                await TryTaskNotification(task.OwnerId, accept ? "Invitation accepted" : "Invitation rejected",
                    $"A collaborator {(accept ? "accepted" : "rejected")} the invitation to '{task.Title}'.", task.Id);
        }
        return result;
    }

    private async Task<IResult> SaveInvitationChange(TaskRequest task, string message, IDbContextTransaction? transaction = null)
    {
        taskShareDal.TouchTask(task);
        try
        {
            // One transaction persists membership, invitation and version. Competing writes roll back.
            await unitOfWork.SaveChangesAsync();
            if (transaction is not null) await transaction.CommitAsync();
            return new SuccessResult(message);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) unitOfWork.DiscardTrackedChanges();
            return new ConflictResult(ConcurrentChange);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) unitOfWork.DiscardTrackedChanges();
            logger.LogWarning("Invitation change could not be saved for task {TaskId}", task.Id);
            return new ErrorResult("The invitation change could not be saved. Refresh and retry.");
        }
    }

    public async Task<IDataResult<List<TaskInvitationDto>>> GetMyPendingInvitationsAsync()
    {
        var now = DateTime.UtcNow;
        var invitations = await unitOfWork.GetRepository<TaskShareInvitation>().GetAllAsync(x =>
            x.InvitedUserId == currentUserService.UserId && x.Status == TaskShareInvitationStatus.Pending &&
            (!x.ExpiresAt.HasValue || x.ExpiresAt > now) && x.TaskRequest.Activity &&
            x.TaskRequest.WorkspaceId == null &&
            x.TaskRequest.Status != TaskStatus.Completed && x.TaskRequest.Status != TaskStatus.Cancelled,
            include: q => q.AsNoTracking().Include(x => x.TaskRequest).Include(x => x.InvitedByUser));
        return new SuccessDataResult<List<TaskInvitationDto>>(invitations.OrderByDescending(x => x.CreatedAt)
            .Select(x => MapInvitation(x, now)).ToList());
    }

    public async Task<IDataResult<TaskInvitationDto>> GetInvitationAsync(int invitationId)
    {
        var userId = currentUserService.UserId;
        // Read the current share and task in the same statement as the invitation.
        var invitation = await unitOfWork.GetRepository<TaskShareInvitation>().GetAsync(x =>
            x.Id == invitationId && x.InvitedUserId == userId,
            include: q => q.AsNoTracking()
                .Include(x => x.TaskRequest).ThenInclude(task => task.TaskShares.Where(share =>
                    share.SharedWithUserId == userId && share.Permission >= TaskPermission.View &&
                    share.Permission <= TaskPermission.Manage))
                .Include(x => x.InvitedByUser).AsSingleQuery());
        return invitation is null
            ? new ErrorDataResult<TaskInvitationDto>(Messages.InvitationNotFound)
            : invitation.TaskRequest.WorkspaceId.HasValue
                ? new ConflictDataResult<TaskInvitationDto>(WorkspaceTaskSharingUnavailable)
                : new SuccessDataResult<TaskInvitationDto>(MapInvitation(invitation, DateTime.UtcNow,
                    invitation.TaskRequest.Activity && (invitation.TaskRequest.OwnerId == userId ||
                        invitation.TaskRequest.TaskShares.Count != 0)));
    }

    private static TaskInvitationDto MapInvitation(TaskShareInvitation invitation, DateTime now, bool hasCurrentAccess = false)
    {
        var pending = invitation.Status == TaskShareInvitationStatus.Pending;
        var expired = pending && IsExpired(invitation, now);
        var available = pending && !expired && CanJoin(invitation.TaskRequest);
        var permission = PermissionName(invitation.Permission);
        var showTask = (available && permission is not null) ||
            (invitation.Status == TaskShareInvitationStatus.Accepted && hasCurrentAccess);
        return new TaskInvitationDto
        {
            Id = invitation.Id, TaskRequestId = showTask ? invitation.TaskRequestId : null,
            TaskTitle = showTask ? invitation.TaskRequest.Title : null,
            InviterUserName = showTask ? invitation.InvitedByUser.UserName : null,
            Permission = permission, CreatedAt = invitation.CreatedAt, ExpiresAt = invitation.ExpiresAt,
            RespondedAt = invitation.RespondedAt, Message = showTask ? invitation.Message : null,
            ResponseMessage = invitation.ResponseMessage,
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
            x.TaskRequest.WorkspaceId == null &&
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
        dto.Message = dto.Message?.Trim();
        if (dto.Message?.Length > TaskShareInvitation.MaxMessageLength)
            return new ErrorResult($"An invitation message may contain at most {TaskShareInvitation.MaxMessageLength} characters.");
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(dto.TaskRequestId);
        if (task is null) return new ErrorResult(Messages.DataNotFound);
        if (task.OwnerId != currentUserService.UserId) return new ErrorResult(Messages.AuthorizationDenied);
        if (task.WorkspaceId.HasValue) return new ConflictResult(WorkspaceTaskSharingUnavailable);
        if (!CanJoin(task)) return new ErrorResult(TaskUnavailable);
        if (dto.Permission is not (TaskPermission.View or TaskPermission.Edit))
            return new ErrorResult("Choose View or Edit access.");
        if (string.IsNullOrWhiteSpace(dto.Username)) return new ErrorResult("Username is required.");
        var users = unitOfWork.GetRepository<User>();
        var normalizedUserName = identityNormalizer.NormalizeUserName(dto.Username);
        var user = await users.GetAsync(x => x.NormalizedUserName == normalizedUserName);
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
            CreatedAt = now, ExpiresAt = now.AddDays(7),
            Message = string.IsNullOrWhiteSpace(dto.Message) ? null : dto.Message
        };
        await invitations.AddAsync(invitation);
        var activity = await activityWriter.WriteAsync(task, currentUserService.UserId,
            TaskActivityType.UserInvited, user.Id, invitation);
        var result = await SaveInvitationChange(task, Messages.TaskShareInvitationSent);
        if (!result.Success) return result;
        await collaborationService.PublishActivityAsync(activity);
        await collaborationService.PublishTaskChangedAsync(task.Id);
        // Delivery is best effort after commit; it cannot turn a persisted invitation into a failed request.
        try
        {
            await notificationService.CreateTaskShareInvitationNotificationAsync(user.Id, task.Title, inviter.UserName, invitation.Id);
        }
        catch (Exception)
        {
            logger.LogWarning("Notification delivery failed for a persisted invitation.");
        }
        try
        {
            var clientUrl = configuration["ClientApp:BaseUrl"];
            if (string.IsNullOrWhiteSpace(clientUrl)) clientUrl = "http://localhost:5173";
            await emailService.SendTaskShareInvitationEmailAsync(user.Email, task.Title, inviter.UserName,
                $"{clientUrl.TrimEnd('/')}/tasks/invitations/{invitation.Id}");
        }
        catch (Exception)
        {
            logger.LogWarning("Email delivery failed for a persisted invitation.");
        }
        return result;
    }

    public async Task<IDataResult<List<OutgoingTaskInvitationDto>>> GetOutgoingInvitationsAsync(int taskId)
    {
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(taskId);
        if (task is null || !task.Activity) return new ErrorDataResult<List<OutgoingTaskInvitationDto>>(Messages.DataNotFound);
        if (task.OwnerId != currentUserService.UserId) return new ErrorDataResult<List<OutgoingTaskInvitationDto>>(Messages.AuthorizationDenied);
        var invitations = await unitOfWork.GetRepository<TaskShareInvitation>().GetAllAsync(x =>
            x.TaskRequestId == taskId,
            q => q.Include(x => x.InvitedUser));
        var now = DateTime.UtcNow;
        return new SuccessDataResult<List<OutgoingTaskInvitationDto>>(invitations.OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new OutgoingTaskInvitationDto
            {
                Id = x.Id, TaskRequestId = x.TaskRequestId, InvitedUserName = x.InvitedUser.UserName,
                Permission = PermissionName(x.Permission),
                Status = x.Status == TaskShareInvitationStatus.Pending && IsExpired(x, now) ? "Expired" : x.Status.ToString(),
                CreatedAt = x.CreatedAt, ExpiresAt = x.ExpiresAt,
                RespondedAt = x.RespondedAt, Message = x.Message, ResponseMessage = x.ResponseMessage,
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
        if (task.WorkspaceId.HasValue) return new ConflictResult(WorkspaceTaskSharingUnavailable);
        if (task.Version != version) return new ConflictResult(ConcurrentChange);
        await taskShareDal.ReloadInvitationAsync(invitation);
        if (invitation.Status != TaskShareInvitationStatus.Pending)
            return new ErrorResult("Only a pending invitation can be cancelled.");
        var now = DateTime.UtcNow;
        if (IsExpired(invitation, now)) return new ErrorResult(Messages.InvitationExpired);
        invitation.Status = TaskShareInvitationStatus.Cancelled;
        invitation.RespondedAt = now;
        var activity = await activityWriter.WriteAsync(task, currentUserService.UserId,
            TaskActivityType.InvitationCancelled, invitation.InvitedUserId, invitation);
        var result = await SaveInvitationChange(task, "Invitation cancelled.");
        if (result.Success)
        {
            await collaborationService.PublishActivityAsync(activity);
            await collaborationService.PublishTaskChangedAsync(task.Id);
        }
        return result;
    }

    public async Task<IResult> UpdateParticipantPermissionAsync(int taskId, int userId, UpdateParticipantPermissionDto dto)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync();
        await subTaskDal.LockParentAsync(taskId, exclusive: true);
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(taskId);
        if (task is null || !task.Activity) return new ErrorResult(Messages.DataNotFound);
        if (task.OwnerId != currentUserService.UserId) return new ErrorResult(Messages.AuthorizationDenied);
        if (task.WorkspaceId.HasValue) return new ConflictResult(WorkspaceTaskSharingUnavailable);
        if (userId == task.OwnerId) return new ErrorResult("The owner is not a removable participant.");
        if (task.Version != dto.Version) return new ConflictResult(ConcurrentChange);
        if (dto.Permission is not (TaskPermission.View or TaskPermission.Edit))
            return new ErrorResult("Choose View or Edit access.");
        var share = await taskShareDal.GetAsync(x => x.TaskRequestId == taskId && x.SharedWithUserId == userId);
        if (share is null) return new ErrorResult("Participant not found.");
        if (share.Permission == dto.Permission) return new SuccessResult("Permission is already set.");
        if (task.Status == TaskStatus.InReview && task.AssigneeUserId == userId &&
            dto.Permission < TaskPermission.Edit)
            return new ConflictResult("The current assignee's working access cannot be reduced while the task is under review.");
        if (dto.Permission < TaskPermission.Edit)
        {
            var assignments = await subTaskDal.GetOpenTaskAssignmentsAsync(taskId, userId);
            if (assignments.Any(x => x.Status == TaskStatus.InReview))
                return new ConflictResult("Working access cannot be reduced while an assigned subtask is under review.");
            await ReleaseSubTaskResponsibilitiesAsync(assignments);
        }
        share.Permission = dto.Permission;
        var activityTime = DateTime.UtcNow;
        TaskActivity? unassigned = null;
        TaskActivity? resetActivity = null;
        if (task.AssigneeUserId == userId && dto.Permission < TaskPermission.Edit)
        {
            task.AssigneeUserId = null;
            var resetsResponsibility = task.Status == TaskStatus.InProgress;
            if (resetsResponsibility) task.Status = TaskStatus.Pending;
            unassigned = await activityWriter.WriteAsync(task, currentUserService.UserId,
                TaskActivityType.UserUnassigned, userId, createdAt: activityTime);
            if (resetsResponsibility)
                resetActivity = await activityWriter.WriteAsync(task, currentUserService.UserId,
                    TaskActivityType.ResponsibilityReset, fromStatus: TaskStatus.InProgress,
                    toStatus: TaskStatus.Pending, createdAt: activityTime);
        }
        var activity = await activityWriter.WriteAsync(task, currentUserService.UserId,
            TaskActivityType.ParticipantPermissionChanged, userId, createdAt: activityTime);
        var result = await SaveInvitationChange(task, "Participant permission updated.", transaction);
        if (!result.Success) return result;
        if (unassigned is not null) await collaborationService.PublishActivityAsync(unassigned);
        if (resetActivity is not null) await collaborationService.PublishActivityAsync(resetActivity);
        await collaborationService.PublishActivityAsync(activity);
        await collaborationService.PublishTaskChangedAsync(task.Id);
        await TryTaskNotification(userId, "Task access changed",
            $"Your access to '{task.Title}' is now {dto.Permission}.", task.Id);
        return result;
    }

    public async Task<IResult> RemoveParticipantAsync(int taskId, int userId, long version)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync();
        await subTaskDal.LockParentAsync(taskId, exclusive: true);
        var task = await unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(taskId);
        if (task is null || !task.Activity) return new ErrorResult(Messages.DataNotFound);
        if (task.OwnerId != currentUserService.UserId) return new ErrorResult(Messages.AuthorizationDenied);
        if (task.WorkspaceId.HasValue) return new ConflictResult(WorkspaceTaskSharingUnavailable);
        if (userId == task.OwnerId) return new ErrorResult("The task owner cannot be removed.");
        if (task.Version != version) return new ConflictResult(ConcurrentChange);
        var assignments = await subTaskDal.GetOpenTaskAssignmentsAsync(taskId, userId);
        if (assignments.Any(x => x.Status == TaskStatus.InReview))
            return new ConflictResult("Access cannot be removed while an assigned subtask is under review.");
        if (task.Status == TaskStatus.InReview)
            return new ErrorResult("Participants cannot be removed while a task is under review.");
        var share = await taskShareDal.GetAsync(x => x.TaskRequestId == taskId && x.SharedWithUserId == userId);
        if (share is null) return new ErrorResult("Participant not found.");
        await ReleaseSubTaskResponsibilitiesAsync(assignments);
        var activityTime = DateTime.UtcNow;
        TaskActivity? unassigned = null;
        TaskActivity? resetActivity = null;
        if (task.AssigneeUserId == userId)
        {
            task.AssigneeUserId = null;
            var resetsResponsibility = task.Status == TaskStatus.InProgress;
            if (resetsResponsibility) task.Status = TaskStatus.Pending;
            unassigned = await activityWriter.WriteAsync(task, currentUserService.UserId,
                TaskActivityType.UserUnassigned, userId, createdAt: activityTime);
            if (resetsResponsibility)
                resetActivity = await activityWriter.WriteAsync(task, currentUserService.UserId,
                    TaskActivityType.ResponsibilityReset, fromStatus: TaskStatus.InProgress,
                    toStatus: TaskStatus.Pending, createdAt: activityTime);
        }
        taskShareDal.Delete(share);
        var activity = await activityWriter.WriteAsync(task, currentUserService.UserId,
            TaskActivityType.ParticipantRemoved, userId, createdAt: activityTime);
        var result = await SaveInvitationChange(task, "Participant removed.", transaction);
        if (!result.Success) return result;
        try { await collaborationService.RevokeAccessAsync(task.Id, userId); }
        catch (Exception) { logger.LogWarning("Could not revoke live workspace access for task {TaskId}", task.Id); }
        if (unassigned is not null) await collaborationService.PublishActivityAsync(unassigned);
        if (resetActivity is not null) await collaborationService.PublishActivityAsync(resetActivity);
        await collaborationService.PublishActivityAsync(activity);
        await collaborationService.PublishTaskChangedAsync(task.Id);
        await TryTaskNotification(userId, "Removed from task", $"You no longer have access to '{task.Title}'.",
            task.Id, "/tasks/shared-tasks");
        return result;
    }

    private async Task ReleaseSubTaskResponsibilitiesAsync(IEnumerable<SubTask> assignments)
    {
        foreach (var subTask in assignments)
            await SubTaskActivityWriter.SetAssigneeAsync(unitOfWork, subTask, currentUserService.UserId, null);
    }

    private async Task TryTaskNotification(int userId, string title, string message, int taskId,
        string? redirectUrl = null)
    {
        try
        {
            await notificationService.CreateTaskNotificationAsync(userId, NotificationType.TaskAccessChanged,
                title, message, taskId, redirectUrl);
        }
        catch (Exception) { logger.LogWarning("Task notification delivery failed."); }
    }
}
