using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;

namespace TaskTracker.Tests;

internal static class WorkspaceTestServices
{
    internal sealed class Realtime(bool fail = false) : ITaskWorkspaceRealtimeService
    {
        public Task ActivityCreatedAsync(int taskId, TaskActivityDto activity) => Deliver();
        public Task MessageCreatedAsync(int taskId, TaskMessageDto message) => Deliver();
        public Task TaskChangedAsync(int taskId) => Deliver();
        public Task AccessRevokedAsync(int taskId, int userId) => Deliver();
        private Task Deliver() => fail ? Task.FromException(new IOException("Delivery unavailable")) : Task.CompletedTask;
    }

    private sealed class Notifications : INotificationService
    {
        public Task CreateTaskShareInvitationNotificationAsync(int userId, string taskTitle, string inviterUserName, int invitationId) => Task.CompletedTask;
        public Task CreateTaskNotificationAsync(int userId, NotificationType type, string title, string message,
            int taskId, string? redirectUrl = null) => Task.CompletedTask;
        public Task<IDataResult<List<NotificationDto>>> GetNotificationsForUserAsync(int userId) =>
            Task.FromResult<IDataResult<List<NotificationDto>>>(new SuccessDataResult<List<NotificationDto>>([]));
        public Task<IResult> MarkAsReadAsync(int notificationId) => Task.FromResult<IResult>(new SuccessResult());
        public Task<IResult> MarkAllAsReadAsync() => Task.FromResult<IResult>(new SuccessResult());
    }

    public static TaskWorkspaceManager Create(TaskTrackerDbContext context, bool failRealtime = false) =>
        new(new UnitOfWork(context), new EfTaskWorkspaceDal(context), new Realtime(failRealtime),
            NullLogger<TaskWorkspaceManager>.Instance);

    public static TaskRequestManager TaskRequests(TaskTrackerDbContext context)
    {
        var uow = new UnitOfWork(context);
        return new TaskRequestManager(uow, new EfTaskShareDal(context), new EfTaskRequestDal(context),
            new TaskActivityWriter(uow), Create(context), new Notifications(), NullLogger<TaskRequestManager>.Instance);
    }
}
