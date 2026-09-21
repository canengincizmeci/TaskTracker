using Microsoft.Extensions.Logging;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.Concrete;

public class TaskWorkspaceManager : ITaskWorkspaceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskWorkspaceDal _workspaceDal;
    private readonly ITaskWorkspaceRealtimeService _realtime;
    private readonly ILogger<TaskWorkspaceManager> _logger;

    public TaskWorkspaceManager(IUnitOfWork unitOfWork, ITaskWorkspaceDal workspaceDal,
        ITaskWorkspaceRealtimeService realtime, ILogger<TaskWorkspaceManager> logger)
    {
        _unitOfWork = unitOfWork;
        _workspaceDal = workspaceDal;
        _realtime = realtime;
        _logger = logger;
    }

    public Task<bool> CanAccessAsync(int taskId, int userId) => _workspaceDal.CanAccessAsync(taskId, userId);

    public async Task<IDataResult<List<TaskActivityDto>>> GetActivitiesAsync(int taskId, int userId)
    {
        if (!await CanAccessAsync(taskId, userId))
            return new ErrorDataResult<List<TaskActivityDto>>(Messages.AuthorizationDenied);
        return new SuccessDataResult<List<TaskActivityDto>>((await _workspaceDal.GetActivitiesAsync(taskId))
            .Select(MapActivity).ToList());
    }

    public async Task<IDataResult<List<TaskMessageDto>>> GetMessagesAsync(int taskId, int userId)
    {
        if (!await CanAccessAsync(taskId, userId))
            return new ErrorDataResult<List<TaskMessageDto>>(Messages.AuthorizationDenied);
        return new SuccessDataResult<List<TaskMessageDto>>((await _workspaceDal.GetMessagesAsync(taskId))
            .Select(MapMessage).ToList());
    }

    public async Task<IDataResult<TaskMessageDto>> SendMessageAsync(int taskId, int userId, string? content)
    {
        if (!await CanAccessAsync(taskId, userId))
            return new ErrorDataResult<TaskMessageDto>(Messages.AuthorizationDenied);
        if (string.IsNullOrWhiteSpace(content) || content.Length > TaskMessage.MaxContentLength)
            return new ErrorDataResult<TaskMessageDto>("Message must contain 1–4000 characters of nonblank text.");
        var sender = await _unitOfWork.GetRepository<User>().GetByIdAsync(userId);
        var message = new TaskMessage
        {
            TaskRequestId = taskId, SenderUserId = userId, SenderUser = sender!,
            Content = content.Trim(), CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.GetRepository<TaskMessage>().AddAsync(message);
        await _unitOfWork.SaveChangesAsync();
        var dto = MapMessage(message);
        try
        {
            await _realtime.MessageCreatedAsync(taskId, dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime delivery failed for persisted task message {MessageId}", message.Id);
        }
        return new SuccessDataResult<TaskMessageDto>(dto);
    }

    // Call only after the business operation has committed. Delivery failure cannot undo persistence.
    public async Task PublishActivityAsync(TaskActivity activity)
    {
        try
        {
            var persisted = await _workspaceDal.GetActivityAsync(activity.Id);
            if (persisted is not null)
                await _realtime.ActivityCreatedAsync(persisted.TaskRequestId, MapActivity(persisted));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime delivery failed for persisted task activity {ActivityId}", activity.Id);
        }
    }

    public async Task PublishTaskChangedAsync(int taskId)
    {
        try
        {
            await _realtime.TaskChangedAsync(taskId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime task refresh delivery failed for task {TaskId}", taskId);
        }
    }

    public Task RevokeAccessAsync(int taskId, int userId) => _realtime.AccessRevokedAsync(taskId, userId);

    private static TaskActivityDto MapActivity(TaskActivity activity) => new()
    {
        Id = activity.Id, ActivityType = activity.ActivityType.ToString(), ActorUserId = activity.ActorUserId,
        ActorUserName = activity.ActorUser.UserName, TargetUserId = activity.TargetUserId,
        TargetUserName = activity.TargetUser?.UserName, FromStatus = activity.FromStatus?.ToString(),
        ToStatus = activity.ToStatus?.ToString(), CreatedAt = activity.CreatedAt
    };

    private static TaskMessageDto MapMessage(TaskMessage message) => new()
    {
        Id = message.Id, SenderUserId = message.SenderUserId, SenderUserName = message.SenderUser.UserName,
        Content = message.Content, CreatedAt = message.CreatedAt
    };
}
