using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Bussiness.ValidationRules.FluentValidation;
using TaskTracker.Core.Aspects.Autofac;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Bussiness.Concrete;

public class TaskRequestManager : ITaskRequestService
{
    private const string ConcurrentChange = "The task changed. Refresh and retry.";
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskShareDal _taskShareDal;
    private readonly ITaskRequestDal _taskRequestDal;
    private readonly ISubTaskDal _subTaskDal;
    private readonly IWorkspaceDal _workspaceDal;
    private readonly ITaskActivityWriter _activityWriter;
    private readonly ITaskCollaborationService _collaboration;
    private readonly INotificationService _notifications;
    private readonly ILogger<TaskRequestManager> _logger;

    public TaskRequestManager(IUnitOfWork unitOfWork, ITaskShareDal taskShareDal, ITaskRequestDal taskRequestDal,
        IWorkspaceDal workspaceDal,
        ITaskActivityWriter activityWriter, ITaskCollaborationService collaboration, INotificationService notifications,
        ILogger<TaskRequestManager> logger, ISubTaskDal subTaskDal)
    {
        _unitOfWork = unitOfWork;
        _taskShareDal = taskShareDal;
        _taskRequestDal = taskRequestDal;
        _subTaskDal = subTaskDal;
        _workspaceDal = workspaceDal;
        _activityWriter = activityWriter;
        _collaboration = collaboration;
        _notifications = notifications;
        _logger = logger;
    }

    [ValidationAspect(typeof(TaskRequestCreateDtoValidator))]
    public async Task<IResult> AddTaskRequestAsync(TaskRequestCreateDto dto, int currentUserId)
    {
        var now = DateTime.UtcNow;
        var task = new TaskRequest
        {
            Title = dto.Title, Description = dto.Description, Category = dto.Category,
            Priority = dto.Priority, Status = TaskStatus.Pending, DueDate = dto.DueDate,
            OwnerId = currentUserId, Activity = true, Visibility = TaskVisibility.Private,
            CreatedAt = now, SharedCount = 0
        };
        await _unitOfWork.GetRepository<TaskRequest>().AddAsync(task);
        if (dto.MlConsentGranted)
            await RecordMlConsentAsync(task, true, currentUserId, now);
        await _unitOfWork.GetRepository<TaskPlanningRevision>().AddAsync(
            CreatePlanningRevision(task, 1, currentUserId, now));
        await _unitOfWork.GetRepository<TaskPlanningSnapshot>().AddAsync(
            CreatePlanningSnapshot(task, dto.UtcOffsetMinutes, now));
        var activity = await _activityWriter.WriteAsync(task, currentUserId, TaskActivityType.TaskCreated,
            createdAt: now);
        await _unitOfWork.SaveChangesAsync();
        await _collaboration.PublishActivityAsync(activity);
        return new SuccessResult(Messages.DataAdded);
    }

    [ValidationAspect(typeof(WorkspaceTaskCreateDtoValidator))]
    public async Task<IDataResult<TaskRequestDto>> AddWorkspaceTaskAsync(int workspaceId,
        WorkspaceTaskCreateDto dto, int currentUserId)
    {
        var creator = await _workspaceDal.GetActiveMembershipAsync(workspaceId, currentUserId);
        if (creator is null) return new ErrorDataResult<TaskRequestDto>(WorkspaceMessages.NotFound);
        if (creator.Role is not (WorkspaceRole.Owner or WorkspaceRole.Admin))
            return new ErrorDataResult<TaskRequestDto>(Messages.AuthorizationDenied);

        WorkspaceMember? assigneeMembership = null;
        if (dto.AssigneeUserId.HasValue)
        {
            assigneeMembership = await _workspaceDal.GetActiveMembershipAsync(workspaceId, dto.AssigneeUserId.Value);
            if (assigneeMembership is null)
                return new ConflictDataResult<TaskRequestDto>("Assignee must be an active member of this workspace.");
        }

        var now = DateTime.UtcNow;
        var task = new TaskRequest
        {
            WorkspaceId = workspaceId, OwnerId = currentUserId, AssigneeUserId = dto.AssigneeUserId,
            Title = dto.Title, Description = dto.Description, Category = dto.Category,
            Priority = dto.Priority, Status = TaskStatus.Pending, DueDate = dto.DueDate,
            Activity = true, Visibility = TaskVisibility.Private, CreatedAt = now, SharedCount = 0
        };
        await _unitOfWork.GetRepository<TaskRequest>().AddAsync(task);
        if (dto.MlConsentGranted)
            await RecordMlConsentAsync(task, true, currentUserId, now);
        await _unitOfWork.GetRepository<TaskPlanningRevision>().AddAsync(
            CreatePlanningRevision(task, 1, currentUserId, now));
        await _unitOfWork.GetRepository<TaskPlanningSnapshot>().AddAsync(
            CreatePlanningSnapshot(task, dto.UtcOffsetMinutes, now));
        var activity = await _activityWriter.WriteAsync(task, currentUserId, TaskActivityType.TaskCreated,
            createdAt: now);
        TaskActivity? assignmentActivity = null;
        if (dto.AssigneeUserId.HasValue)
            assignmentActivity = await _activityWriter.WriteAsync(task, currentUserId,
                TaskActivityType.UserAssigned, dto.AssigneeUserId.Value, createdAt: now);
        _workspaceDal.TouchMembership(creator);
        if (assigneeMembership is not null && assigneeMembership.Id != creator.Id)
            _workspaceDal.TouchMembership(assigneeMembership);
        try { await _unitOfWork.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        { return new ConflictDataResult<TaskRequestDto>(WorkspaceMessages.ConcurrentChange); }
        await _collaboration.PublishActivityAsync(activity);
        if (assignmentActivity is not null) await _collaboration.PublishActivityAsync(assignmentActivity);
        return await GetTaskById(task.Id, currentUserId);
    }

    public async Task<IDataResult<TaskRequestDto>> GetTaskById(int taskId, int currentUserId)
    {
        var task = await _unitOfWork.GetRepository<TaskRequest>().GetAsync(x => x.Id == taskId,
            q => q.Include(x => x.Owner).Include(x => x.Assignee).Include(x => x.Workspace));
        if (task is null || !task.Activity) return new ErrorDataResult<TaskRequestDto>(Messages.DataNotFound);
        var workspaceMembership = task.WorkspaceId.HasValue
            ? await _workspaceDal.GetActiveMembershipAsync(task.WorkspaceId.Value, currentUserId)
            : null;
        var share = task.WorkspaceId is null && task.OwnerId != currentUserId
            ? await _taskShareDal.GetAsync(x => x.TaskRequestId == taskId && x.SharedWithUserId == currentUserId)
            : null;
        var validShare = share is not null && Enum.IsDefined(share.Permission);
        var isOwner = task.OwnerId == currentUserId;
        var canView = task.WorkspaceId.HasValue
            ? workspaceMembership is not null
            : isOwner || task.Visibility == TaskVisibility.Public || validShare;
        if (!canView) return new ErrorDataResult<TaskRequestDto>(Messages.AuthorizationDenied);
        var canEdit = (task.WorkspaceId.HasValue ? isOwner && workspaceMembership is not null :
                isOwner || validShare && share!.Permission >= TaskPermission.Edit) &&
            task.Status is not (TaskStatus.Completed or TaskStatus.Cancelled or TaskStatus.InReview);
        var validAssignee = task.AssigneeUserId.HasValue && task.AssigneeUserId != task.OwnerId &&
            (task.WorkspaceId.HasValue
                ? await _workspaceDal.GetActiveMembershipAsync(task.WorkspaceId.Value, task.AssigneeUserId.Value) is not null
                : await _taskShareDal.HasPermissionAsync(task.Id, task.AssigneeUserId.Value, TaskPermission.Edit));
        var latest = task.Status == TaskStatus.InReview ? await _taskRequestDal.GetLatestSubmissionAsync(task.Id) : null;
        var latestReviewed = latest is not null && await _unitOfWork.GetRepository<TaskSubmissionReview>()
            .AnyAsync(x => x.TaskSubmissionId == latest.Id);
        var canReadWorkflow = task.WorkspaceId.HasValue ? workspaceMembership is not null : isOwner || validShare;
        var workflowSubmissionId = !canReadWorkflow ? null : task.Status switch
        {
            TaskStatus.InReview when validAssignee && latest?.SubmittedByUserId == task.AssigneeUserId && !latestReviewed
                => latest?.Id,
            TaskStatus.InProgress when validAssignee
                => await _taskRequestDal.GetSubmissionForLatestTransitionAsync(task.Id, TaskActivityType.ChangesRequested),
            TaskStatus.Completed
                => await _taskRequestDal.GetSubmissionForLatestTransitionAsync(task.Id, TaskActivityType.SubmissionApproved),
            _ => null
        };
        return new SuccessDataResult<TaskRequestDto>(new TaskRequestDto
        {
            Id = task.Id, OwnerId = task.OwnerId, OwnerUserName = task.Owner.UserName,
            WorkspaceId = task.WorkspaceId, WorkspaceName = task.Workspace?.Name,
            AssigneeUserId = task.AssigneeUserId, AssigneeUserName = task.Assignee?.UserName,
            Version = task.Version, Title = task.Title, Description = task.Description,
            Category = task.Category, Priority = task.Priority.ToString(), Status = task.Status.ToString(),
            Activity = task.Activity, DueDate = task.DueDate, Visibility = task.Visibility.ToString(),
            CreatedAt = task.CreatedAt, IsOwner = isOwner, IsAssignee = task.AssigneeUserId == currentUserId,
            MlConsent = isOwner ? new TaskMlConsentDto(
                task.MlConsentGranted && task.MlConsentPolicyVersion == TaskMlConsentEvent.CurrentPolicyVersion,
                task.MlConsentGrantedAt, task.MlConsentRevokedAt, task.MlConsentPolicyVersion) : null,
            CanView = canView, CanEdit = canEdit,
            CanShare = isOwner && task.WorkspaceId is null,
            CanDelete = isOwner && (task.WorkspaceId is null || workspaceMembership is not null),
            CanViewParticipants = canReadWorkflow,
            CanManageResponsibility = isOwner && (task.WorkspaceId is null || workspaceMembership is not null),
            CanStart = task.Status == TaskStatus.Pending && (task.AssigneeUserId.HasValue
                ? task.AssigneeUserId == currentUserId && (task.WorkspaceId.HasValue
                    ? workspaceMembership is not null
                    : isOwner || validShare && share!.Permission >= TaskPermission.Edit)
                : isOwner && (task.WorkspaceId is null || workspaceMembership is not null)),
            CanSubmit = !isOwner && task.AssigneeUserId == currentUserId && validAssignee &&
                task.Status == TaskStatus.InProgress,
            CanReview = isOwner && task.Status == TaskStatus.InReview && workflowSubmissionId.HasValue,
            WorkflowSubmissionId = workflowSubmissionId,
            CurrentUserPermission = isOwner ? "Owner" : validShare ? share!.Permission.ToString() : null
        });
    }

    public async Task<IResult> UpdateMlConsentAsync(int taskId, UpdateTaskMlConsentDto dto, int currentUserId)
    {
        if (dto.Granted is not bool granted || dto.Version is null or < 0)
            return new ErrorResult("A consent decision and a valid task version are required.");

        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        await _subTaskDal.LockParentWorkspaceAsync(taskId);
        await _subTaskDal.LockParentAsync(taskId, exclusive: true);
        var task = await _unitOfWork.GetRepository<TaskRequest>().GetAsync(x => x.Id == taskId && x.Activity);
        if (task is null || task.OwnerId != currentUserId ||
            (task.WorkspaceId.HasValue &&
             await _workspaceDal.GetActiveMembershipAsync(task.WorkspaceId.Value, currentUserId) is null))
            return new ErrorResult(Messages.AuthorizationDenied);

        if (task.Version != dto.Version)
            return new ConflictResult(ConcurrentChange);

        if (task.MlConsentGranted == granted &&
            (!granted || task.MlConsentPolicyVersion == TaskMlConsentEvent.CurrentPolicyVersion))
            return new SuccessResult(Messages.DataUpdated);

        await RecordMlConsentAsync(task, granted, currentUserId, DateTime.UtcNow);
        try
        {
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _unitOfWork.DiscardTrackedChanges();
            return new ConflictResult(ConcurrentChange);
        }
        return new SuccessResult(Messages.DataUpdated);
    }

    private async Task RecordMlConsentAsync(TaskRequest task, bool granted, int actorId, DateTime now)
    {
        task.MlConsentGranted = granted;
        if (granted)
        {
            task.MlConsentGrantedAt = now;
            task.MlConsentRevokedAt = null;
            task.MlConsentPolicyVersion = TaskMlConsentEvent.CurrentPolicyVersion;
        }
        else
        {
            task.MlConsentRevokedAt = now;
        }

        await _unitOfWork.GetRepository<TaskMlConsentEvent>().AddAsync(new TaskMlConsentEvent
        {
            TaskRequest = task,
            ActorUserId = actorId,
            Granted = granted,
            OccurredAtUtc = now,
            PolicyVersion = task.MlConsentPolicyVersion!
        });
    }

    [ValidationAspect(typeof(UpdateTaskRequestDtoValidator))]
    public async Task<IResult> UpdateTask(UpdateTaskRequestDto dto, int currentUserId)
    {
        var task = await _unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(dto.Id);
        if (task is null || !task.Activity) return new ErrorResult(Messages.DataNotFound);
        if (task.Version != dto.Version) return new ConflictResult(ConcurrentChange);
        if (task.Status is TaskStatus.Completed or TaskStatus.Cancelled or TaskStatus.InReview)
            return new ErrorResult("Task details cannot be changed in the current state.");
        if (!await _taskRequestDal.CanEditAsync(task.Id, currentUserId))
            return new ErrorResult(Messages.AuthorizationDenied);
        if (dto.DueDate.HasValue && dto.DueDate != task.DueDate &&
            dto.DueDate.Value < TaskCalendar.LocalDay(DateTime.UtcNow, dto.UtcOffsetMinutes))
            return new ErrorResult("Due date must be today or later when changed.");
        if (task.Title == dto.Title && task.Description == dto.Description && task.Category == dto.Category &&
            task.Priority == dto.Priority && task.DueDate == dto.DueDate) return new SuccessResult(Messages.DataUpdated);
        var revisionNumber = await _taskRequestDal.GetLatestPlanningRevisionNumberAsync(task.Id) + 1;
        var changedAt = DateTime.UtcNow;
        task.Title = dto.Title; task.Description = dto.Description; task.Category = dto.Category;
        task.Priority = dto.Priority; task.DueDate = dto.DueDate;
        await _unitOfWork.GetRepository<TaskPlanningRevision>().AddAsync(
            CreatePlanningRevision(task, revisionNumber, currentUserId, changedAt));
        var activity = await _activityWriter.WriteAsync(task, currentUserId, TaskActivityType.TaskDetailsUpdated,
            createdAt: changedAt);
        return await SaveTaskChange(task, activity, Messages.DataUpdated);
    }

    public async Task<IResult> AssignTaskAsync(int taskId, AssignTaskDto dto, int currentUserId)
    {
        var task = await GetActiveTask(taskId);
        if (task is null) return new ErrorResult(Messages.DataNotFound);
        if (!await _taskRequestDal.CanManageAsync(task.Id, currentUserId))
            return new ErrorResult(Messages.AuthorizationDenied);
        if (task.Version != dto.Version) return new ConflictResult(ConcurrentChange);
        if (task.Status is TaskStatus.Completed or TaskStatus.Cancelled or TaskStatus.InReview)
            return new ErrorResult("Responsibility cannot be changed in the current state.");
        WorkspaceMember? assigneeMembership = null;
        if (task.WorkspaceId.HasValue)
        {
            assigneeMembership = await _workspaceDal.GetActiveMembershipAsync(task.WorkspaceId.Value,
                dto.AssigneeUserId);
            if (assigneeMembership is null)
                return new ConflictResult("Assignee must be an active member of this workspace.");
        }
        else if (dto.AssigneeUserId != task.OwnerId &&
                 !await _taskShareDal.HasPermissionAsync(task.Id, dto.AssigneeUserId, TaskPermission.Edit))
            return new ErrorResult("Assignee must be the owner or an accepted collaborator with Edit access.");
        if (task.AssigneeUserId == dto.AssigneeUserId) return new SuccessResult("Task is already assigned to this user.");
        var previous = task.AssigneeUserId;
        var resetsResponsibility = task.Status == TaskStatus.InProgress;
        if (resetsResponsibility) task.Status = TaskStatus.Pending;
        task.AssigneeUserId = dto.AssigneeUserId;
        if (assigneeMembership is not null) _workspaceDal.TouchMembership(assigneeMembership);
        var activityTime = DateTime.UtcNow;
        TaskActivity? previousActivity = null;
        if (previous.HasValue)
            previousActivity = await _activityWriter.WriteAsync(task, currentUserId, TaskActivityType.UserUnassigned,
                previous, createdAt: activityTime);
        TaskActivity? resetActivity = null;
        if (resetsResponsibility)
            resetActivity = await _activityWriter.WriteAsync(task, currentUserId, TaskActivityType.ResponsibilityReset,
                fromStatus: TaskStatus.InProgress, toStatus: TaskStatus.Pending, createdAt: activityTime);
        var activity = await _activityWriter.WriteAsync(task, currentUserId, TaskActivityType.UserAssigned,
            dto.AssigneeUserId, createdAt: activityTime);
        var result = await SaveTaskChange(task, activity, "Task assigned successfully.", publishActivity: false);
        if (!result.Success) return result;
        if (previousActivity is not null) await _collaboration.PublishActivityAsync(previousActivity);
        if (resetActivity is not null) await _collaboration.PublishActivityAsync(resetActivity);
        await _collaboration.PublishActivityAsync(activity);
        await NotifyAssignment(task, previous, dto.AssigneeUserId);
        return result;
    }

    public async Task<IResult> UnassignTaskAsync(int taskId, TaskWorkflowCommandDto dto, int currentUserId)
    {
        var task = await GetActiveTask(taskId);
        if (task is null) return new ErrorResult(Messages.DataNotFound);
        if (!await _taskRequestDal.CanManageAsync(task.Id, currentUserId))
            return new ErrorResult(Messages.AuthorizationDenied);
        if (task.Version != dto.Version) return new ConflictResult(ConcurrentChange);
        if (task.Status == TaskStatus.InReview) return new ErrorResult("A task under review cannot be unassigned.");
        if (!task.AssigneeUserId.HasValue) return new SuccessResult("Task is already unassigned.");
        var previous = task.AssigneeUserId.Value;
        task.AssigneeUserId = null;
        var resetsResponsibility = task.Status == TaskStatus.InProgress;
        if (resetsResponsibility) task.Status = TaskStatus.Pending;
        var activityTime = DateTime.UtcNow;
        var activity = await _activityWriter.WriteAsync(task, currentUserId, TaskActivityType.UserUnassigned, previous,
            createdAt: activityTime);
        TaskActivity? resetActivity = null;
        if (resetsResponsibility)
            resetActivity = await _activityWriter.WriteAsync(task, currentUserId, TaskActivityType.ResponsibilityReset,
                fromStatus: TaskStatus.InProgress, toStatus: TaskStatus.Pending, createdAt: activityTime);
        var result = await SaveTaskChange(task, resetActivity ?? activity, "Task unassigned successfully.",
            publishActivity: false);
        if (!result.Success) return result;
        await _collaboration.PublishActivityAsync(activity);
        if (resetActivity is not null) await _collaboration.PublishActivityAsync(resetActivity);
        if (result.Success && previous != task.OwnerId)
            await TryNotify(previous, NotificationType.TaskAssignment, "Task unassigned",
                $"You are no longer assigned to '{task.Title}'.", task.Id);
        return result;
    }

    public async Task<IResult> StartTaskAsync(int taskId, TaskWorkflowCommandDto dto, int currentUserId)
    {
        var task = await GetActiveTask(taskId);
        if (task is null) return new ErrorResult(Messages.DataNotFound);
        if (task.Version != dto.Version) return new ConflictResult(ConcurrentChange);
        if (task.Status != TaskStatus.Pending) return new ConflictResult("Task must be Pending for this action.");
        var allowed = task.AssigneeUserId.HasValue ? task.AssigneeUserId == currentUserId : task.OwnerId == currentUserId;
        if (!allowed) return new ErrorResult(Messages.AuthorizationDenied);
        if (task.WorkspaceId.HasValue)
        {
            var membership = await _workspaceDal.GetActiveMembershipAsync(task.WorkspaceId.Value, currentUserId);
            if (membership is null) return new ErrorResult(Messages.AuthorizationDenied);
            _workspaceDal.TouchMembership(membership);
        }
        else if (task.AssigneeUserId.HasValue && task.AssigneeUserId != task.OwnerId &&
                 !await _taskShareDal.HasPermissionAsync(task.Id, currentUserId, TaskPermission.Edit))
            return new ErrorResult(Messages.AuthorizationDenied);
        return await ApplyStatus(task, currentUserId, TaskStatus.InProgress,
            TaskActivityType.WorkStarted, "Work started.");
    }

    public async Task<IResult> CompleteTaskAsync(int taskId, TaskWorkflowCommandDto dto, int currentUserId)
    {
        var task = await GetActiveTask(taskId);
        if (task is null) return new ErrorResult(Messages.DataNotFound);
        if (task.Version != dto.Version) return new ConflictResult(ConcurrentChange);
        if (!await _taskRequestDal.CanManageAsync(task.Id, currentUserId))
            return new ErrorResult(Messages.AuthorizationDenied);
        if (task.AssigneeUserId.HasValue && task.AssigneeUserId != task.OwnerId)
            return new ErrorResult("Delegated work will be completed through submission and review.");
        if (task.Status is not (TaskStatus.Pending or TaskStatus.InProgress))
            return new ErrorResult("Only pending or in-progress personal work can be completed.");
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        await _subTaskDal.LockParentAsync(taskId, exclusive: true);
        if (await _subTaskDal.HasOpenAsync(taskId))
            return new ConflictResult("Resolve all active subtasks before completing the parent task.");
        return await ApplyStatus(task, currentUserId, TaskStatus.Completed, TaskActivityType.TaskCompleted,
            "Task completed.", transaction);
    }

    public async Task<IResult> CancelTaskAsync(int taskId, TaskWorkflowCommandDto dto, int currentUserId)
    {
        var task = await GetActiveTask(taskId);
        if (task is null) return new ErrorResult(Messages.DataNotFound);
        if (task.Version != dto.Version) return new ConflictResult(ConcurrentChange);
        if (!await _taskRequestDal.CanManageAsync(task.Id, currentUserId))
            return new ErrorResult(Messages.AuthorizationDenied);
        if (task.Status is not (TaskStatus.Pending or TaskStatus.InProgress or TaskStatus.InReview))
            return new ConflictResult("Only pending, in-progress, or in-review tasks can be cancelled.");
        return await ApplyStatus(task, currentUserId, TaskStatus.Cancelled, TaskActivityType.TaskCancelled, "Task cancelled.");
    }

    public async Task<IResult> ReopenTaskAsync(int taskId, TaskWorkflowCommandDto dto, int currentUserId)
    {
        var task = await GetActiveTask(taskId);
        if (task is null) return new ErrorResult(Messages.DataNotFound);
        if (task.Version != dto.Version) return new ConflictResult(ConcurrentChange);
        if (!await _taskRequestDal.CanManageAsync(task.Id, currentUserId))
            return new ErrorResult(Messages.AuthorizationDenied);
        if (task.Status is not (TaskStatus.Completed or TaskStatus.Cancelled))
            return new ErrorResult("Only completed or cancelled tasks can be reopened.");
        if (task.WorkspaceId.HasValue)
        {
            var ownerMembership = await _workspaceDal.GetActiveMembershipAsync(task.WorkspaceId.Value, task.OwnerId);
            if (ownerMembership is null)
                return new ConflictResult("The task owner is no longer an active workspace member.");
            _workspaceDal.TouchMembership(ownerMembership);
            if (task.AssigneeUserId.HasValue && task.AssigneeUserId != task.OwnerId)
            {
                var assigneeMembership = await _workspaceDal.GetActiveMembershipAsync(task.WorkspaceId.Value,
                    task.AssigneeUserId.Value);
                if (assigneeMembership is null)
                    return new ConflictResult("The task assignee is no longer an active workspace member.");
                _workspaceDal.TouchMembership(assigneeMembership);
            }
        }
        return await ApplyStatus(task, currentUserId, TaskStatus.Pending, TaskActivityType.TaskReopened, "Task reopened.");
    }

    public async Task<IDataResult<TaskSubmissionDto>> SubmitAsync(int taskId, CreateTaskSubmissionDto dto,
        int currentUserId)
    {
        var task = await GetActiveTask(taskId);
        if (task is null) return new ErrorDataResult<TaskSubmissionDto>(Messages.DataNotFound);
        if (task.Version != dto.Version) return new ConflictDataResult<TaskSubmissionDto>(ConcurrentChange);
        if (task.Status != TaskStatus.InProgress)
            return new ConflictDataResult<TaskSubmissionDto>("Only in-progress delegated work can be submitted.");
        if (!task.AssigneeUserId.HasValue || task.AssigneeUserId == task.OwnerId ||
            task.AssigneeUserId != currentUserId)
            return new ErrorDataResult<TaskSubmissionDto>(Messages.AuthorizationDenied);
        if (task.WorkspaceId.HasValue)
        {
            var membership = await _workspaceDal.GetActiveMembershipAsync(task.WorkspaceId.Value, currentUserId);
            if (membership is null) return new ErrorDataResult<TaskSubmissionDto>(Messages.AuthorizationDenied);
            _workspaceDal.TouchMembership(membership);
        }
        else if (!await _taskShareDal.HasPermissionAsync(task.Id, currentUserId, TaskPermission.Edit))
            return new ErrorDataResult<TaskSubmissionDto>(Messages.AuthorizationDenied);
        var content = dto.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content) || content.Length > TaskSubmission.MaxContentLength)
            return new ErrorDataResult<TaskSubmissionDto>("Submission must contain 1–10000 characters of nonblank text.");

        var submittedAt = DateTime.UtcNow;
        var submission = new TaskSubmission
        {
            TaskRequestId = task.Id, TaskRequest = task, SubmittedByUserId = currentUserId,
            SubmittedByUser = task.Assignee!, RevisionNumber = await _taskRequestDal.GetLatestRevisionNumberAsync(task.Id) + 1,
            Content = content, CreatedAt = submittedAt
        };
        await _unitOfWork.GetRepository<TaskSubmission>().AddAsync(submission);
        task.Status = TaskStatus.InReview;
        var activity = await _activityWriter.WriteAsync(task, currentUserId, TaskActivityType.SubmissionCreated,
            fromStatus: TaskStatus.InProgress, toStatus: TaskStatus.InReview, submission: submission,
            createdAt: submittedAt);
        try { await _unitOfWork.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return new ConflictDataResult<TaskSubmissionDto>(ConcurrentChange); }
        catch (DbUpdateException ex) when (IsUniqueConflict(ex))
        { return new ConflictDataResult<TaskSubmissionDto>("The task was submitted by another request. Refresh and retry."); }

        await _collaboration.PublishActivityAsync(activity);
        await _collaboration.PublishTaskChangedAsync(task.Id);
        await TryNotify(task.OwnerId, NotificationType.TaskUpdated, "Task submitted for review",
            $"Revision {submission.RevisionNumber} of '{task.Title}' is waiting for review.", task.Id);
        return new SuccessDataResult<TaskSubmissionDto>(MapSubmission(submission), "Work submitted for review.");
    }

    public async Task<IDataResult<List<TaskSubmissionDto>>> GetSubmissionHistoryAsync(int taskId, int currentUserId)
    {
        if (!await _collaboration.CanAccessAsync(taskId, currentUserId))
            return new ErrorDataResult<List<TaskSubmissionDto>>(Messages.AuthorizationDenied);
        return new SuccessDataResult<List<TaskSubmissionDto>>(await _taskRequestDal.GetSubmissionHistoryAsync(taskId));
    }

    public async Task<IDataResult<TaskSubmissionReviewDto>> ReviewAsync(int taskId, int submissionId,
        ReviewTaskSubmissionDto dto, int currentUserId)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        // Both decisions lock the parent before writing review evidence. This keeps the order
        // consistent when approval also has to verify that all subtasks are resolved.
        await _subTaskDal.LockParentAsync(taskId, exclusive: true);
        var task = await GetActiveTask(taskId);
        if (task is null) return new ErrorDataResult<TaskSubmissionReviewDto>(Messages.DataNotFound);
        if (!await _taskRequestDal.CanManageAsync(task.Id, currentUserId))
            return new ErrorDataResult<TaskSubmissionReviewDto>(Messages.AuthorizationDenied);
        if (task.Version != dto.Version) return new ConflictDataResult<TaskSubmissionReviewDto>(ConcurrentChange);
        if (task.Status != TaskStatus.InReview)
            return new ConflictDataResult<TaskSubmissionReviewDto>("This submission is no longer actionable.");
        if (!Enum.IsDefined(dto.Decision))
            return new ErrorDataResult<TaskSubmissionReviewDto>("Review decision must be Approved or ChangesRequested.");
        var feedback = dto.Feedback?.Trim();
        if (feedback?.Length > TaskSubmissionReview.MaxFeedbackLength ||
            dto.Decision == TaskReviewDecision.ChangesRequested && string.IsNullOrWhiteSpace(feedback))
            return new ErrorDataResult<TaskSubmissionReviewDto>(
                "Changes requested requires nonblank feedback up to 5000 characters.");

        var latest = await _taskRequestDal.GetLatestSubmissionAsync(task.Id);
        var validAssignee = task.AssigneeUserId.HasValue && task.AssigneeUserId != task.OwnerId &&
            (task.WorkspaceId.HasValue
                ? await _workspaceDal.GetActiveMembershipAsync(task.WorkspaceId.Value, task.AssigneeUserId.Value) is not null
                : await _taskShareDal.HasPermissionAsync(task.Id, task.AssigneeUserId.Value, TaskPermission.Edit));
        if (latest is null || latest.Id != submissionId || latest.SubmittedByUserId != task.AssigneeUserId ||
            !validAssignee ||
            await _unitOfWork.GetRepository<TaskSubmissionReview>().AnyAsync(x => x.TaskSubmissionId == submissionId))
            return new ConflictDataResult<TaskSubmissionReviewDto>("This submission is old, reviewed, or no longer actionable.");

        var approving = dto.Decision == TaskReviewDecision.Approved;
        if (approving && await _subTaskDal.HasOpenAsync(taskId))
            return new ConflictDataResult<TaskSubmissionReviewDto>(
                "Resolve all active subtasks before completing the parent task.");

        var reviewedAt = DateTime.UtcNow;
        var review = new TaskSubmissionReview
        {
            TaskSubmissionId = latest.Id, TaskSubmission = latest, ReviewerUserId = currentUserId,
            ReviewerUser = task.Owner, Decision = dto.Decision, Feedback = feedback, CreatedAt = reviewedAt
        };
        await _unitOfWork.GetRepository<TaskSubmissionReview>().AddAsync(review);
        var from = task.Status;
        task.Status = approving ? TaskStatus.Completed : TaskStatus.InProgress;
        var type = approving
            ? TaskActivityType.SubmissionApproved : TaskActivityType.ChangesRequested;
        var activity = await _activityWriter.WriteAsync(task, currentUserId, type, fromStatus: from,
            toStatus: task.Status, submission: latest, review: review, createdAt: reviewedAt);
        try
        {
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _unitOfWork.DiscardTrackedChanges();
            return new ConflictDataResult<TaskSubmissionReviewDto>(ConcurrentChange);
        }
        catch (DbUpdateException ex) when (IsUniqueConflict(ex))
        {
            _unitOfWork.DiscardTrackedChanges();
            return new ConflictDataResult<TaskSubmissionReviewDto>("This submission was already reviewed.");
        }

        await _collaboration.PublishActivityAsync(activity);
        await _collaboration.PublishTaskChangedAsync(task.Id);
        await TryNotify(latest.SubmittedByUserId, NotificationType.TaskUpdated,
            dto.Decision == TaskReviewDecision.Approved ? "Submission approved" : "Changes requested",
            dto.Decision == TaskReviewDecision.Approved
                ? $"Revision {latest.RevisionNumber} of '{task.Title}' was approved."
                : $"Changes were requested for revision {latest.RevisionNumber} of '{task.Title}'.", task.Id);
        return new SuccessDataResult<TaskSubmissionReviewDto>(MapReview(review),
            dto.Decision == TaskReviewDecision.Approved ? "Submission approved." : "Changes requested.");
    }

    public async Task<IDataResult<List<AwaitingReviewTaskDto>>> GetAwaitingReviewAsync(int currentUserId) =>
        new SuccessDataResult<List<AwaitingReviewTaskDto>>(await _taskRequestDal.GetAwaitingReviewAsync(currentUserId));

    private async Task<IResult> ApplyStatus(TaskRequest task, int actorId, TaskStatus to,
        TaskActivityType type, string message, IDbContextTransaction? transaction = null)
    {
        var from = task.Status;
        task.Status = to;
        var activity = await _activityWriter.WriteAsync(task, actorId, type, fromStatus: from, toStatus: to);
        var result = await SaveTaskChange(task, activity, message, transaction: transaction);
        if (!result.Success) return result;
        if (type == TaskActivityType.WorkStarted && actorId != task.OwnerId)
            await TryNotify(task.OwnerId, NotificationType.TaskUpdated, "Work started",
                $"Work started on '{task.Title}'.", task.Id);
        if (type is TaskActivityType.TaskCancelled or TaskActivityType.TaskReopened &&
            task.AssigneeUserId.HasValue && task.AssigneeUserId != actorId)
            await TryNotify(task.AssigneeUserId.Value, NotificationType.TaskUpdated,
                type == TaskActivityType.TaskCancelled ? "Task cancelled" : "Task reopened",
                $"'{task.Title}' was {(type == TaskActivityType.TaskCancelled ? "cancelled" : "reopened")}.", task.Id);
        return result;
    }

    public async Task<IResult> DeleteTask(int taskId, int currentUserId)
    {
        var task = await _unitOfWork.GetRepository<TaskRequest>().GetByIdAsync(taskId);
        if (task is null || !task.Activity) return new ErrorResult(Messages.DataNotFound);
        if (!await _taskRequestDal.CanManageAsync(task.Id, currentUserId))
            return new ErrorResult(Messages.AuthorizationDenied);
        var now = DateTime.UtcNow;
        task.Activity = false;
        task.DeletedAt = now;
        task.DeletedByUserId = currentUserId;
        var activity = await _activityWriter.WriteAsync(task, currentUserId, TaskActivityType.TaskDeleted,
            createdAt: now);
        return await SaveTaskChange(task, activity, Messages.DataUpdated);
    }

    public async Task<IDataResult<List<GetTasksDto>>> GetTasksByUserId(int userId) =>
        new SuccessDataResult<List<GetTasksDto>>((await _taskRequestDal.GetTasksByUserIdAsync(userId))
            .Select(x => MapList(x, userId)).ToList(), Messages.DataListed);

    public async Task<IDataResult<List<GetTasksDto>>> GetWorkspaceTasksAsync(int workspaceId, int currentUserId)
    {
        if (await _workspaceDal.GetActiveMembershipAsync(workspaceId, currentUserId) is null)
            return new ErrorDataResult<List<GetTasksDto>>(WorkspaceMessages.NotFound);

        return new SuccessDataResult<List<GetTasksDto>>(
            await _taskRequestDal.GetWorkspaceTasksAsync(workspaceId, currentUserId), Messages.DataListed);
    }

    public async Task<IDataResult<List<GetTasksDto>>> GetAssignedTasksAsync(int userId) =>
        new SuccessDataResult<List<GetTasksDto>>((await _taskRequestDal.GetAssignedTasksAsync(userId))
            .Select(x => MapList(x, userId)).ToList(), Messages.DataListed);

    private static GetTasksDto MapList(TaskRequest task, int userId)
    {
        var share = task.TaskShares.FirstOrDefault(x => x.SharedWithUserId == userId && Enum.IsDefined(x.Permission));
        var owner = task.OwnerId == userId;
        var workspaceAccess = task.WorkspaceId.HasValue;
        return new GetTasksDto
        {
            Id = task.Id, OwnerId = task.OwnerId, OwnerUserName = task.Owner.UserName,
            WorkspaceId = task.WorkspaceId, WorkspaceName = task.Workspace?.Name,
            AssigneeUserId = task.AssigneeUserId, AssigneeUserName = task.Assignee?.UserName,
            Title = task.Title, Description = task.Description, Category = task.Category,
            Priority = task.Priority.ToString(), Status = task.Status.ToString(), Activity = task.Activity,
            DueDate = task.DueDate, IsOwner = owner, IsSharedWithMe = share is not null,
            CanView = workspaceAccess || owner || share is not null,
            CanEdit = workspaceAccess ? owner : owner || share?.Permission >= TaskPermission.Edit,
            CanShare = owner && !workspaceAccess, Visibility = task.Visibility.ToString(), CreatedAt = task.CreatedAt,
            SharedCount = task.SharedCount, Version = task.Version
        };
    }

    public async Task<IDataResult<List<TaskRequest>>> GetAllTasks() =>
        new SuccessDataResult<List<TaskRequest>>(await _unitOfWork.GetRepository<TaskRequest>().GetAllAsync(x => x.Activity));

    private Task<TaskRequest?> GetActiveTask(int taskId) => _unitOfWork.GetRepository<TaskRequest>().GetAsync(
        x => x.Id == taskId && x.Activity,
        q => q.Include(x => x.Owner).Include(x => x.Assignee).Include(x => x.Workspace));

    private async Task<IResult> SaveTaskChange(TaskRequest task, TaskActivity activity, string message,
        bool publishActivity = true, IDbContextTransaction? transaction = null)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync();
            if (transaction is not null) await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) _unitOfWork.DiscardTrackedChanges();
            return new ConflictResult(ConcurrentChange);
        }
        catch (DbUpdateException ex) when (IsPlanningRevisionUniqueConflict(ex))
        { return new ConflictResult(ConcurrentChange); }
        if (publishActivity) await _collaboration.PublishActivityAsync(activity);
        await _collaboration.PublishTaskChangedAsync(task.Id);
        return new SuccessResult(message);
    }

    private async Task NotifyAssignment(TaskRequest task, int? previous, int current)
    {
        if (previous.HasValue && previous != current && previous != task.OwnerId)
            await TryNotify(previous.Value, NotificationType.TaskAssignment, "Task reassigned",
                $"You are no longer assigned to '{task.Title}'.", task.Id);
        if (current != task.OwnerId)
            await TryNotify(current, NotificationType.TaskAssignment, "Task assigned to you",
                $"You are now responsible for '{task.Title}'.", task.Id);
    }

    private async Task TryNotify(int userId, NotificationType type, string title, string message, int taskId)
    {
        try { await _notifications.CreateTaskNotificationAsync(userId, type, title, message, taskId); }
        catch (Exception)
        {
            _logger.LogWarning("Task notification delivery failed for notification type {NotificationType}.", type);
        }
    }

    private static TaskSubmissionDto MapSubmission(TaskSubmission submission) => new()
    {
        Id = submission.Id, RevisionNumber = submission.RevisionNumber,
        SubmittedByUserId = submission.SubmittedByUserId,
        SubmittedByUserName = submission.SubmittedByUser.UserName,
        Content = submission.Content, CreatedAt = submission.CreatedAt
    };

    private static TaskSubmissionReviewDto MapReview(TaskSubmissionReview review) => new()
    {
        Id = review.Id, ReviewerUserId = review.ReviewerUserId,
        ReviewerUserName = review.ReviewerUser.UserName, Decision = review.Decision.ToString(),
        Feedback = review.Feedback, CreatedAt = review.CreatedAt
    };

    private static TaskPlanningRevision CreatePlanningRevision(TaskRequest task, int revisionNumber,
        int changedByUserId, DateTime changedAt) => new()
    {
        TaskRequest = task,
        RevisionNumber = revisionNumber,
        ChangedByUserId = changedByUserId,
        ChangedAt = changedAt,
        Title = task.Title,
        Description = task.Description,
        Category = task.Category,
        Priority = task.Priority,
        DueDate = task.DueDate
    };

    private static TaskPlanningSnapshot CreatePlanningSnapshot(TaskRequest task, int utcOffsetMinutes,
        DateTime createdAtUtc)
    {
        DateTime? originalDueAtUtc = task.DueDate.HasValue
            ? TaskPlanningSnapshot.ComputeOriginalDueAtUtc(task.DueDate.Value, utcOffsetMinutes)
            : null;
        int? plannedCalendarDays = task.DueDate.HasValue
            ? task.DueDate.Value.DayNumber - TaskCalendar.LocalDay(createdAtUtc, utcOffsetMinutes).DayNumber
            : null;
        long? plannedDurationSeconds = originalDueAtUtc.HasValue
            ? (long)Math.Round((originalDueAtUtc.Value - createdAtUtc).TotalSeconds, MidpointRounding.AwayFromZero)
            : null;
        var assignmentState = !task.AssigneeUserId.HasValue
            ? TaskInitialAssignmentState.Unassigned
            : task.AssigneeUserId.Value == task.OwnerId
                ? TaskInitialAssignmentState.SelfAssigned
                : TaskInitialAssignmentState.Delegated;

        return new TaskPlanningSnapshot
        {
            TaskRequest = task,
            SchemaVersion = TaskPlanningSnapshot.CurrentSchemaVersion,
            CreatedAtUtc = createdAtUtc,
            InitialUtcOffsetMinutes = utcOffsetMinutes,
            InitialPriority = task.Priority,
            InitialDueDate = task.DueDate,
            OriginalDueAtUtc = originalDueAtUtc,
            InitialPlannedCalendarDays = plannedCalendarDays,
            InitialPlannedDurationSeconds = plannedDurationSeconds,
            InitialAssignmentState = assignmentState,
            IsWorkspaceTask = task.WorkspaceId.HasValue,
            InitialVisibility = task.Visibility,
            InitialTitleLength = task.Title.Length,
            InitialDescriptionLength = task.Description.Length
        };
    }

    private static bool IsUniqueConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsPlanningRevisionUniqueConflict(DbUpdateException exception)
    {
        const string constraintName = "IX_TaskPlanningRevisions_TaskRequestId_RevisionNumber";
        if (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: constraintName
            })
            return true;

        var message = exception.InnerException?.Message;
        return message?.Contains("TaskPlanningRevisions.TaskRequestId", StringComparison.OrdinalIgnoreCase) == true &&
               message.Contains("TaskPlanningRevisions.RevisionNumber", StringComparison.OrdinalIgnoreCase);
    }
}
