using Microsoft.Extensions.Logging;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Concrete;

public class TaskCollaborationManager : ITaskCollaborationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskCollaborationDal _collaborationDal;
    private readonly ITaskCollaborationRealtimeService _realtime;
    private readonly ILogger<TaskCollaborationManager> _logger;
    private readonly ISubTaskDal _subTasks;

    public TaskCollaborationManager(IUnitOfWork unitOfWork, ITaskCollaborationDal collaborationDal,
        ITaskCollaborationRealtimeService realtime, ILogger<TaskCollaborationManager> logger, ISubTaskDal subTasks)
    {
        _unitOfWork = unitOfWork;
        _collaborationDal = collaborationDal;
        _realtime = realtime;
        _logger = logger;
        _subTasks = subTasks;
    }

    public Task<bool> CanAccessAsync(int taskId, int userId, CancellationToken ct = default) =>
        _collaborationDal.CanAccessAsync(taskId, userId, ct);

    public async Task<IDataResult<List<TaskActivityDto>>> GetActivitiesAsync(int taskId, int userId)
    {
        if (!await CanAccessAsync(taskId, userId))
            return new ErrorDataResult<List<TaskActivityDto>>(Messages.AuthorizationDenied);
        return new SuccessDataResult<List<TaskActivityDto>>((await _collaborationDal.GetActivitiesAsync(taskId))
            .Select(MapActivity).ToList());
    }

    public async Task<IDataResult<List<TaskMessageDto>>> GetMessagesAsync(int taskId, int userId)
    {
        if (!await CanAccessAsync(taskId, userId))
            return new ErrorDataResult<List<TaskMessageDto>>(Messages.AuthorizationDenied);
        return new SuccessDataResult<List<TaskMessageDto>>((await _collaborationDal.GetMessagesAsync(taskId))
            .Select(TaskMessageDto.From).ToList());
    }

    public async Task<IDataResult<TaskMessageDto>> SendMessageAsync(int taskId, int userId, string? content)
    {
        TaskMessage message;
        await using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            await _subTasks.LockParentWorkspaceAsync(taskId);
            await _subTasks.LockParentAsync(taskId, exclusive: false);
            if (!await CanAccessAsync(taskId, userId))
                return new ErrorDataResult<TaskMessageDto>(Messages.AuthorizationDenied);
            if (string.IsNullOrWhiteSpace(content) || content.Length > TaskMessage.MaxContentLength)
                return new ErrorDataResult<TaskMessageDto>("Message must contain 1–4000 characters of nonblank text.");
            var sender = await _unitOfWork.GetRepository<User>().GetByIdAsync(userId);
            message = new TaskMessage
            {
                TaskRequestId = taskId, SenderUserId = userId, SenderUser = sender!,
                Content = content.Trim(), CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.GetRepository<TaskMessage>().AddAsync(message);
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        var dto = TaskMessageDto.From(message);
        await PublishMessageAsync(taskId, dto);
        return new SuccessDataResult<TaskMessageDto>(dto);
    }

    public async Task PublishMessageAsync(int taskId, TaskMessageDto message)
    {
        try
        {
            await _realtime.MessageCreatedAsync(taskId, message);
        }
        catch (Exception)
        {
            _logger.LogWarning("Realtime delivery failed for a persisted task message.");
        }
    }

    // Call only after the business operation has committed. Delivery failure cannot undo persistence.
    public async Task PublishActivityAsync(TaskActivity activity)
    {
        try
        {
            var persisted = await _collaborationDal.GetActivityAsync(activity.Id);
            if (persisted is not null)
                await _realtime.ActivityCreatedAsync(persisted.TaskRequestId, MapActivity(persisted));
        }
        catch (Exception)
        {
            _logger.LogWarning("Realtime delivery failed for a persisted task activity.");
        }
    }

    public async Task PublishTaskChangedAsync(int taskId)
    {
        try
        {
            await _realtime.TaskChangedAsync(taskId);
        }
        catch (Exception)
        {
            _logger.LogWarning("Realtime task refresh delivery failed for task {TaskId}", taskId);
        }
    }

    public Task RevokeAccessAsync(int taskId, int userId) => _realtime.AccessRevokedAsync(taskId, userId);

    public async Task PublishAttachmentsChangedAsync(int taskId)
    {
        try { await _realtime.AttachmentsChangedAsync(taskId); }
        catch (Exception) { _logger.LogWarning("Realtime attachment refresh failed for task {TaskId}", taskId); }
    }

    public async Task PublishWorkspaceRosterChangedAsync(int workspaceId, IReadOnlyList<int> taskIds)
    {
        if (taskIds.Count == 0) return;
        try { await _realtime.WorkspaceRosterChangedAsync(workspaceId, taskIds); }
        catch (Exception) { _logger.LogWarning("Realtime roster refresh failed for workspace {WorkspaceId}", workspaceId); }
    }

    private static TaskActivityDto MapActivity(TaskActivity activity) => new()
    {
        Id = activity.Id, ActivityType = activity.ActivityType.ToString(), ActorUserId = activity.ActorUserId,
        ActorUserName = activity.ActorUser.UserName, TargetUserId = activity.TargetUserId,
        TargetUserName = activity.TargetUser?.UserName, FromStatus = activity.FromStatus?.ToString(),
        ToStatus = activity.ToStatus?.ToString(), SubmissionId = activity.SubmissionId,
        ReviewId = activity.ReviewId, RevisionNumber = activity.Submission?.RevisionNumber,
        CreatedAt = activity.CreatedAt
    };

}
