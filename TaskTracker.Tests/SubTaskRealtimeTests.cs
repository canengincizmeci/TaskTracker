using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.API.Hubs;
using TaskTracker.API.Services;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.DataAccess.Concrete.EfCore;
using static TaskTracker.Tests.SubTaskWorkflowTests;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class SubTaskRealtimeTests
{
    [Fact]
    public async Task Committed_subtask_changes_publish_only_the_parent_id_on_the_existing_group()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context);
        var events = new List<(string Group, string Method, object?[] Arguments, TaskStatus Status)>();
        var hub = new TestHubContext(new TestHubClients(async (group, method, arguments) =>
        {
            await using var verify = database.CreateContext();
            var persisted = await verify.SubTasks.AsNoTracking().SingleAsync();
            events.Add((group, method, arguments, persisted.Status));
        }));
        var collaboration = new TaskCollaborationManager(new UnitOfWork(context), new EfTaskCollaborationDal(context),
            new SignalRTaskCollaborationService(hub), NullLogger<TaskCollaborationManager>.Instance, new EfSubTaskDal(context));
        var service = TaskCollaborationTestServices.SubTasks(context, collaboration);
        var created = Data(await service.CreateAsync(1, Details("Sensitive title"), 1));
        var started = Data(await service.StartAsync(1, created.Id, Version(created.Version), 1));
        Assert.False((await service.StartAsync(1, created.Id, Version(created.Version), 1)).Success);
        Assert.True((await service.GetAsync(1, created.Id, 2)).Success);
        Assert.True((await service.UpdateAsync(1, created.Id, new()
        {
            Version = started.Version, Title = started.Title, Description = started.Description
        }, 1)).Success);

        Assert.Equal([TaskStatus.Pending, TaskStatus.InProgress], events.Select(x => x.Status));
        Assert.All(events, entry =>
        {
            Assert.Equal("task:1", entry.Group);
            Assert.Equal("TaskChanged", entry.Method);
            Assert.Equal(1, Assert.Single(entry.Arguments));
        });
    }

    private sealed class TestHubContext(IHubClients clients) : IHubContext<TaskCollaborationHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class TestHubClients(Func<string, string, object?[], Task> send) : IHubClients
    {
        public IClientProxy Group(string groupName) => new TestClient((method, args) => send(groupName, method, args));
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class TestClient(Func<string, object?[], Task> send) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            send(method, args);
    }
}
