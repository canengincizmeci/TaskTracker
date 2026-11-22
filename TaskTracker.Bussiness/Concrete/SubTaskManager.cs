using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Bussiness.ValidationRules.FluentValidation;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Bussiness.Concrete;

public class SubTaskManager(IUnitOfWork unitOfWork, ISubTaskDal subTasks, ITaskShareDal shares,
    IWorkspaceDal workspaces, ITaskCollaborationService collaboration, INotificationService notifications,
    ILogger<SubTaskManager> logger) : ISubTaskService
{
    private const string ConcurrentChange = "The subtask changed. Refresh and retry.";
    private const string LockedParent = "The parent task is locked for subtask changes.";
    private const string InvalidState = "The subtask is in a state that does not allow this action.";
    private const string InvalidId = "Resource identifiers must be positive.";

    public async Task<IDataResult<SubTaskDto>> CreateAsync(int taskId, CreateSubTaskDto dto, int userId)
    {
        var error = SubTaskValidators.Details.Validate(dto).Errors.FirstOrDefault()?.ErrorMessage;
        if (error is not null) return new ErrorDataResult<SubTaskDto>(error);
        if (DueDateIsPast(dto)) return new ErrorDataResult<SubTaskDto>("Due date must be today or later.");
        if (dto.AssigneeUserId is <= 0) return new ErrorDataResult<SubTaskDto>("Assignee identifiers must be positive.");

        var result = await WriteAsync<SubTaskDto>(taskId, userId, async parent =>
        {
            if (parent.OwnerId != userId)
                return (new ErrorDataResult<SubTaskDto>(Messages.AuthorizationDenied), false);
            if (IsParentLocked(parent))
                return (new ConflictDataResult<SubTaskDto>(LockedParent), false);
            if (dto.AssigneeUserId is int assigneeId && !await IsEligibleAsync(parent, assigneeId))
                return (new ConflictDataResult<SubTaskDto>("The selected user is ineligible for subtask responsibility."), false);
            var subTask = new SubTask
            {
                TaskRequestId = parent.Id, CreatedByUserId = userId,
                Title = dto.Title.Trim(), Description = dto.Description.Trim(), Priority = dto.Priority,
                DueDate = dto.DueDate
            };
            await unitOfWork.GetRepository<SubTask>().AddAsync(subTask);
            await RecordAsync(subTask, userId, TaskActivityType.TaskCreated);
            if (dto.AssigneeUserId.HasValue)
                await SubTaskActivityWriter.SetAssigneeAsync(unitOfWork, subTask, userId, dto.AssigneeUserId);
            await unitOfWork.SaveChangesAsync();
            return (new SuccessDataResult<SubTaskDto>(await subTasks.GetDetailAsync(taskId, subTask.Id, userId)), true);
        });
        if (result.Success)
            await NotifyAssignmentAsync(result.Data, userId, previous: null);
        return result;
    }

    public Task<IDataResult<List<SubTaskDto>>> ListAsync(int taskId, int userId) =>
        ReadAsync(taskId, null, userId, () => subTasks.ListAsync(taskId, userId));

    public async Task<IDataResult<List<AssignedSubTaskDto>>> ListAssignedAsync(int userId) =>
        new SuccessDataResult<List<AssignedSubTaskDto>>(await subTasks.ListAssignedAsync(userId));

    public Task<IDataResult<SubTaskDto>> GetAsync(int taskId, int subTaskId, int userId) =>
        ReadAsync(taskId, subTaskId, userId, () => subTasks.GetDetailAsync(taskId, subTaskId, userId));

    public Task<IDataResult<List<SubTaskSubmissionDto>>> GetSubmissionHistoryAsync(int taskId, int subTaskId, int userId) =>
        ReadAsync(taskId, subTaskId, userId, () => subTasks.GetSubmissionsAsync(taskId, subTaskId));

    public Task<IDataResult<List<SubTaskActivityDto>>> GetActivityHistoryAsync(int taskId, int subTaskId, int userId) =>
        ReadAsync(taskId, subTaskId, userId, () => subTasks.GetActivitiesAsync(taskId, subTaskId));

    public Task<IDataResult<SubTaskDto>> UpdateAsync(int taskId, int subTaskId, UpdateSubTaskDto dto, int userId)
    {
        var error = SubTaskValidators.Details.Validate(dto).Errors.FirstOrDefault()?.ErrorMessage;
        if (error is not null) return Invalid<SubTaskDto>(error);
        return ChangeAsync(taskId, subTaskId, new SubTaskCommandDto { Version = dto.Version }, userId,
            async (_, subTask) =>
            {
                if (!IsEditable(subTask)) return new ConflictResult(InvalidState);
                if (dto.DueDate != subTask.DueDate && DueDateIsPast(dto))
                    return new ErrorResult("Due date must be today or later when changed.");
                var title = dto.Title.Trim();
                var description = dto.Description.Trim();
                if (subTask.Title == title && subTask.Description == description && subTask.Priority == dto.Priority &&
                    subTask.DueDate == dto.DueDate)
                    return new SuccessResult();
                subTask.Title = title;
                subTask.Description = description;
                subTask.Priority = dto.Priority;
                subTask.DueDate = dto.DueDate;
                await RecordAsync(subTask, userId, TaskActivityType.TaskDetailsUpdated);
                return new SuccessResult();
            });
    }

    public async Task<IResult> DeleteAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId) =>
        await ChangeAsync(taskId, subTaskId, dto, userId, ownerOnly: true, async (_, subTask) =>
        {
            subTask.Activity = false;
            subTask.DeletedAt = DateTime.UtcNow;
            subTask.DeletedByUserId = userId;
            await RecordAsync(subTask, userId, TaskActivityType.TaskDeleted);
            return new SuccessResult();
        }, () => Task.FromResult(true));

    public Task<IDataResult<SubTaskDto>> AssignAsync(int taskId, int subTaskId, AssignSubTaskDto dto, int userId)
    {
        var error = SubTaskValidators.Assignment.Validate(dto).Errors.FirstOrDefault()?.ErrorMessage;
        return error is null ? ChangeAssigneeAsync(taskId, subTaskId, dto, userId, dto.AssigneeUserId)
            : Invalid<SubTaskDto>(error);
    }

    public Task<IDataResult<SubTaskDto>> UnassignAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId) =>
        ChangeAssigneeAsync(taskId, subTaskId, dto, userId, null);

    private Task<IDataResult<SubTaskDto>> ChangeAssigneeAsync(int taskId, int subTaskId, SubTaskCommandDto dto,
        int userId, int? assigneeId)
    {
        int? previous = null;
        return ChangeAsync(taskId, subTaskId, dto, userId, async (parent, subTask) =>
        {
            if (!IsEditable(subTask)) return new ConflictResult(InvalidState);
            if (assigneeId.HasValue && !await IsEligibleAsync(parent, assigneeId.Value))
                return new ConflictResult("The selected user is ineligible for subtask responsibility.");
            previous = subTask.AssigneeUserId;
            await SubTaskActivityWriter.SetAssigneeAsync(unitOfWork, subTask, userId, assigneeId);
            return new SuccessResult();
        }, onCommitted: item => NotifyAssignmentAsync(item, userId, previous));
    }

    public Task<IDataResult<SubTaskDto>> StartAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId) =>
        ChangeAsync(taskId, subTaskId, dto, userId, async (parent, subTask) =>
        {
            if ((subTask.AssigneeUserId ?? parent.OwnerId) != userId || !await IsEligibleAsync(parent, userId))
                return new ErrorResult(Messages.AuthorizationDenied);
            if (subTask.Status != TaskStatus.Pending) return new ConflictResult(InvalidState);
            return await TransitionAsync(subTask, userId, TaskStatus.InProgress, TaskActivityType.WorkStarted);
        }, ownerOnly: false);

    public Task<IDataResult<SubTaskDto>> CompleteAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId) =>
        ChangeAsync(taskId, subTaskId, dto, userId, (parent, subTask) =>
        {
            if (subTask.AssigneeUserId.HasValue && subTask.AssigneeUserId != parent.OwnerId)
                return Task.FromResult<IResult>(new ConflictResult("Delegated work requires submission and review."));
            return IsEditable(subTask)
                ? TransitionAsync(subTask, userId, TaskStatus.Completed, TaskActivityType.TaskCompleted)
                : Task.FromResult<IResult>(new ConflictResult(InvalidState));
        });

    public Task<IDataResult<SubTaskDto>> CancelAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId) =>
        ChangeAsync(taskId, subTaskId, dto, userId, (_, subTask) =>
            subTask.Status is TaskStatus.Pending or TaskStatus.InProgress or TaskStatus.InReview
                ? TransitionAsync(subTask, userId, TaskStatus.Cancelled, TaskActivityType.TaskCancelled)
                : Task.FromResult<IResult>(new ConflictResult(InvalidState)));

    public Task<IDataResult<SubTaskDto>> ReopenAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId) =>
        ChangeAsync(taskId, subTaskId, dto, userId, async (parent, subTask) =>
        {
            if (subTask.Status is not (TaskStatus.Completed or TaskStatus.Cancelled))
                return new ConflictResult(InvalidState);
            if (subTask.AssigneeUserId.HasValue && !await IsEligibleAsync(parent, subTask.AssigneeUserId.Value))
                await SubTaskActivityWriter.SetAssigneeAsync(unitOfWork, subTask, userId, null);
            return await TransitionAsync(subTask, userId, TaskStatus.Pending, TaskActivityType.TaskReopened);
        });

    public Task<IDataResult<SubTaskDto>> SubmitAsync(int taskId, int subTaskId, CreateSubTaskSubmissionDto dto, int userId)
    {
        var error = SubTaskValidators.Submission.Validate(dto).Errors.FirstOrDefault()?.ErrorMessage;
        if (error is not null) return Invalid<SubTaskDto>(error);
        var ownerId = 0;
        return ChangeAsync(taskId, subTaskId, dto, userId, async (parent, subTask) =>
        {
            if (subTask.AssigneeUserId != userId || parent.OwnerId == userId || !await IsEligibleAsync(parent, userId))
                return new ErrorResult(Messages.AuthorizationDenied);
            if (subTask.Status != TaskStatus.InProgress) return new ConflictResult(InvalidState);
            ownerId = parent.OwnerId;

            var latest = await subTasks.GetLatestSubmissionAsync(taskId, subTaskId);
            var submission = new SubTaskSubmission
            {
                SubTask = subTask, TaskRequestId = taskId, SubmittedByUserId = userId,
                Content = dto.Content.Trim(), RevisionNumber = checked((latest?.RevisionNumber ?? 0) + 1)
            };
            await unitOfWork.GetRepository<SubTaskSubmission>().AddAsync(submission);
            return await TransitionAsync(subTask, userId, TaskStatus.InReview, TaskActivityType.SubmissionCreated, submission);
        }, ownerOnly: false, onCommitted: item => TryNotifyAsync(item, ownerId, NotificationType.TaskUpdated,
            "Subtask awaiting review", "A subtask submission is ready for your review."));
    }

    public Task<IDataResult<SubTaskDto>> ReviewAsync(int taskId, int subTaskId, int submissionId,
        ReviewSubTaskSubmissionDto dto, int userId)
    {
        if (submissionId <= 0) return Invalid<SubTaskDto>(InvalidId);
        var error = SubTaskValidators.Review.Validate(dto).Errors.FirstOrDefault()?.ErrorMessage;
        if (error is not null) return Invalid<SubTaskDto>(error);
        return ChangeAsync(taskId, subTaskId, dto, userId, async (parent, subTask) =>
        {
            if (subTask.Status != TaskStatus.InReview)
                return new ConflictResult(InvalidState);
            var latest = await GetActionableSubmissionAsync(parent, subTaskId, subTask.AssigneeUserId, submissionId);
            if (latest is null)
                return new ConflictResult("This submission is no longer actionable.");

            var review = new SubTaskSubmissionReview
            {
                SubTaskSubmission = latest, ReviewerUserId = userId, Decision = dto.Decision,
                Feedback = string.IsNullOrWhiteSpace(dto.Feedback) ? null : dto.Feedback.Trim()
            };
            await unitOfWork.GetRepository<SubTaskSubmissionReview>().AddAsync(review);
            var approved = dto.Decision == TaskReviewDecision.Approved;
            return await TransitionAsync(subTask, userId, approved ? TaskStatus.Completed : TaskStatus.InProgress,
                approved ? TaskActivityType.SubmissionApproved : TaskActivityType.ChangesRequested, latest, review);
        }, onCommitted: item => item.AssigneeUserId is int assignee ? TryNotifyAsync(item, assignee,
            NotificationType.TaskUpdated,
            dto.Decision == TaskReviewDecision.Approved ? "Subtask approved" : "Subtask changes requested",
            dto.Decision == TaskReviewDecision.Approved ? "Your subtask submission was approved." :
                "The owner requested changes to your subtask submission.") : Task.CompletedTask);
    }

    private async Task<SubTaskSubmission?> GetActionableSubmissionAsync(TaskRequest parent, int subTaskId,
        int? assigneeId, int submissionId)
    {
        var latest = await subTasks.GetLatestSubmissionAsync(parent.Id, subTaskId);
        if (latest is null || latest.Id != submissionId || latest.SubmittedByUserId != assigneeId ||
            latest.SubmittedByUserId == parent.OwnerId || !await IsEligibleAsync(parent, latest.SubmittedByUserId) ||
            await unitOfWork.GetRepository<SubTaskSubmissionReview>().AnyAsync(x => x.SubTaskSubmissionId == submissionId))
            return null;
        return latest;
    }

    private async Task<IDataResult<T>> ReadAsync<T>(int taskId, int? subTaskId, int userId, Func<Task<T>> query)
    {
        if (taskId <= 0 || subTaskId is <= 0) return new ErrorDataResult<T>(InvalidId);
        await using var transaction = await unitOfWork.BeginTransactionAsync();
        await LockScopeAsync(taskId);
        if (!await collaboration.CanAccessAsync(taskId, userId))
            return new ErrorDataResult<T>(Messages.DataNotFound);
        if (subTaskId.HasValue)
        {
            await subTasks.LockSubTaskForReadAsync(taskId, subTaskId.Value);
            if (!await subTasks.HasActiveAsync(taskId, subTaskId.Value))
                return new ErrorDataResult<T>(Messages.DataNotFound);
        }
        var data = await query();
        await transaction.CommitAsync();
        return new SuccessDataResult<T>(data);
    }

    private async Task<IDataResult<SubTaskDto>> ChangeAsync(int taskId, int subTaskId, SubTaskCommandDto dto, int userId,
        Func<TaskRequest, SubTask, Task<IResult>> apply, bool ownerOnly = true, Func<SubTaskDto, Task>? onCommitted = null)
    {
        var result = await ChangeAsync(taskId, subTaskId, dto, userId, ownerOnly, apply,
            () => subTasks.GetDetailAsync(taskId, subTaskId, userId));
        if (result.Success && onCommitted is not null) await onCommitted(result.Data);
        return result;
    }

    private Task<IDataResult<T>> ChangeAsync<T>(int taskId, int subTaskId, SubTaskCommandDto dto, int userId,
        bool ownerOnly, Func<TaskRequest, SubTask, Task<IResult>> apply, Func<Task<T>> read)
    {
        if (subTaskId <= 0) return Invalid<T>(InvalidId);
        var error = SubTaskValidators.Command.Validate(dto).Errors.FirstOrDefault()?.ErrorMessage;
        if (error is not null) return Invalid<T>(error);
        return WriteAsync<T>(taskId, userId, async parent =>
        {
            var subTask = await subTasks.GetActiveAsync(taskId, subTaskId);
            if (subTask is null) return (new ErrorDataResult<T>(Messages.DataNotFound), false);
            if (ownerOnly && parent.OwnerId != userId)
                return (new ErrorDataResult<T>(Messages.AuthorizationDenied), false);
            if (IsParentLocked(parent))
                return (new ConflictDataResult<T>(LockedParent), false);
            if (subTask.Version != dto.Version) return (new ConflictDataResult<T>(ConcurrentChange), false);
            var result = await apply(parent, subTask);
            if (!result.Success)
                return (result is IConflictResult ? new ConflictDataResult<T>(result.Message) :
                    new ErrorDataResult<T>(result.Message), false);
            var changed = await unitOfWork.SaveChangesAsync() > 0;
            return (new SuccessDataResult<T>(await read()), changed);
        });
    }

    private async Task<IDataResult<T>> WriteAsync<T>(int taskId, int userId,
        Func<TaskRequest, Task<(IDataResult<T> Result, bool Changed)>> apply)
    {
        if (taskId <= 0) return new ErrorDataResult<T>(InvalidId);
        IDataResult<T> result;
        bool changed;
        try
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync();
            await LockScopeAsync(taskId);
            var parent = await subTasks.GetActiveParentAsync(taskId);
            if (parent is null || !await collaboration.CanAccessAsync(taskId, userId))
                return new ErrorDataResult<T>(Messages.DataNotFound);
            (result, changed) = await apply(parent);
            if (!result.Success) return result;
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            unitOfWork.DiscardTrackedChanges();
            return new ConflictDataResult<T>(ConcurrentChange);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                   { SqlState: PostgresErrorCodes.UniqueViolation, TableName: "SubTaskSubmissions" or "SubTaskSubmissionReviews" })
        {
            unitOfWork.DiscardTrackedChanges();
            return new ConflictDataResult<T>(ConcurrentChange);
        }
        if (changed) await collaboration.PublishTaskChangedAsync(taskId);
        return result;
    }

    private async Task LockScopeAsync(int taskId)
    {
        // Take the workspace lock first. Parent writes and personal revocation conflict
        // with the shared parent lock, while sibling subtasks can still change in parallel.
        await subTasks.LockParentWorkspaceAsync(taskId);
        await subTasks.LockParentAsync(taskId, exclusive: false);
    }

    private async Task NotifyAssignmentAsync(SubTaskDto item, int actor, int? previous)
    {
        if (previous == item.AssigneeUserId) return;
        if (previous is int prior && prior != actor)
            await TryNotifyAsync(item, prior, NotificationType.TaskAssignment, "Subtask unassigned",
                "You are no longer assigned to a subtask.");
        if (item.AssigneeUserId is int assignee && assignee != actor)
            await TryNotifyAsync(item, assignee, NotificationType.TaskAssignment, "Subtask assigned",
                "A subtask was assigned to you.");
    }

    private async Task TryNotifyAsync(SubTaskDto item, int recipient, NotificationType type, string title, string message)
    {
        try
        {
            await notifications.CreateTaskNotificationAsync(recipient, type, title, message, item.TaskRequestId,
                $"/tasks/task-detail/{item.TaskRequestId}?subtask={item.Id}");
        }
        catch (Exception ex)
        {
            unitOfWork.DiscardTrackedChanges();
            logger.LogWarning(ex, "Subtask notification failed for subtask {SubTaskId} and recipient {UserId}", item.Id, recipient);
        }
    }

    private Task<bool> IsEligibleAsync(TaskRequest parent, int userId) =>
        parent.WorkspaceId.HasValue ? IsActiveMemberAsync(parent.WorkspaceId.Value, userId)
        : parent.OwnerId == userId ? Task.FromResult(true) : shares.HasPermissionAsync(parent.Id, userId, TaskPermission.Edit);

    private async Task<bool> IsActiveMemberAsync(int workspaceId, int userId) =>
        await workspaces.GetActiveMembershipAsync(workspaceId, userId) is not null;

    private async Task<IResult> TransitionAsync(SubTask subTask, int actorId, TaskStatus status, TaskActivityType type,
        SubTaskSubmission? submission = null, SubTaskSubmissionReview? review = null)
    {
        var previous = subTask.Status;
        subTask.Status = status;
        await SubTaskActivityWriter.WriteAsync(unitOfWork, subTask, actorId, type, from: previous, to: status,
            submission: submission, review: review);
        return new SuccessResult();
    }

    private Task RecordAsync(SubTask subTask, int actorId, TaskActivityType type) =>
        SubTaskActivityWriter.WriteAsync(unitOfWork, subTask, actorId, type);

    private static bool IsEditable(SubTask subTask) => subTask.Status is TaskStatus.Pending or TaskStatus.InProgress;

    private static bool IsParentLocked(TaskRequest parent) =>
        parent.Status is TaskStatus.Completed or TaskStatus.Cancelled or TaskStatus.InReview;

    private static bool DueDateIsPast(CreateSubTaskDto dto) =>
        dto.DueDate.HasValue && dto.DueDate.Value < TaskCalendar.LocalDay(DateTime.UtcNow, dto.UtcOffsetMinutes);

    private static Task<IDataResult<T>> Invalid<T>(string message) =>
        Task.FromResult<IDataResult<T>>(new ErrorDataResult<T>(message));
}
