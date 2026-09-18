using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

internal static class WorkspaceTestServices
{
    internal sealed class Realtime(bool fail = false) : ITaskWorkspaceRealtimeService
    {
        public Task ActivityCreatedAsync(int taskId, TaskActivityDto activity) => Deliver();
        public Task MessageCreatedAsync(int taskId, TaskMessageDto message) => Deliver();
        private Task Deliver() => fail ? Task.FromException(new IOException("Delivery unavailable")) : Task.CompletedTask;
    }

    public static TaskWorkspaceManager Create(TaskTrackerDbContext context, bool failRealtime = false) =>
        new(new UnitOfWork(context), new EfTaskWorkspaceDal(context), new Realtime(failRealtime),
            NullLogger<TaskWorkspaceManager>.Instance);

    public static TaskRequestManager TaskRequests(TaskTrackerDbContext context)
    {
        var uow = new UnitOfWork(context);
        return new TaskRequestManager(uow, new EfTaskShareDal(context), new EfTaskRequestDal(context),
            new TaskActivityWriter(uow), Create(context));
    }
}
