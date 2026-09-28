using Microsoft.EntityFrameworkCore;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Tests;

public class TaskCollaborationTests
{
    [Fact]
    public async Task Collaboration_requires_owner_or_valid_membership_even_for_public_tasks()
    {
        using var database = new TestDatabase(); using var context = database.CreateContext();
        var task = TestDatabase.Task(); task.Visibility = TaskVisibility.Public;
        context.TaskRequests.Add(task);
        context.TaskShareInvitations.Add(new() { TaskRequestId = 1, InvitedByUserId = 1, InvitedUserId = 3, Permission = TaskPermission.Edit });
        context.TaskShares.Add(new() { TaskRequestId = 1, SharedWithUserId = 2, Permission = TaskPermission.View });
        await context.SaveChangesAsync();
        var collaboration = TaskCollaborationTestServices.Create(context);
        Assert.True(await collaboration.CanAccessAsync(1, 1));
        Assert.True(await collaboration.CanAccessAsync(1, 2));
        Assert.False(await collaboration.CanAccessAsync(1, 3));
        Assert.False((await collaboration.GetActivitiesAsync(1, 3)).Success);
        Assert.False((await collaboration.GetMessagesAsync(1, 3)).Success);
        Assert.False((await collaboration.SendMessageAsync(1, 3, "Unauthorized")).Success);
        Assert.True((await collaboration.SendMessageAsync(1, 2, "View participant can discuss")).Success);
        (await context.TaskShares.SingleAsync()).Permission = (TaskPermission)0;
        await context.SaveChangesAsync();
        Assert.False(await collaboration.CanAccessAsync(1, 2));
    }

    [Fact]
    public async Task Messages_validate_content_and_persist_despite_realtime_failure()
    {
        using var database = new TestDatabase(); using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task()); await context.SaveChangesAsync();
        var collaboration = TaskCollaborationTestServices.Create(context, failRealtime: true);
        Assert.False((await collaboration.SendMessageAsync(1, 1, " \t\n")).Success);
        Assert.False((await collaboration.SendMessageAsync(1, 1, new string('a', 4001))).Success);
        var sent = await collaboration.SendMessageAsync(1, 1, "Persisted discussion");
        Assert.True(sent.Success);
        using var fresh = database.CreateContext();
        var message = Assert.Single((await TaskCollaborationTestServices.Create(fresh).GetMessagesAsync(1, 1)).Data);
        Assert.Equal(sent.Data.Id, message.Id);
        Assert.Equal("user1", message.SenderUserName);
        Assert.Equal("Persisted discussion", message.Content);
    }

    [Fact]
    public async Task Activity_writer_defers_save_and_task_creation_records_structured_history()
    {
        using var database = new TestDatabase(); using var context = database.CreateContext();
        var manager = TaskCollaborationTestServices.TaskRequests(context);
        Assert.True((await manager.AddTaskRequestAsync(new() { Title = "History", Description = "History", Category = "Test" }, 1)).Success);
        var created = await context.TaskActivities.SingleAsync();
        Assert.Equal(TaskActivityType.TaskCreated, created.ActivityType);
        var task = await context.TaskRequests.SingleAsync();
        await new TaskActivityWriter(new UnitOfWork(context)).WriteAsync(task, 1, TaskActivityType.TaskDetailsUpdated);
        using var fresh = database.CreateContext();
        Assert.Single(await fresh.TaskActivities.ToListAsync());
        await context.SaveChangesAsync();
        var history = (await TaskCollaborationTestServices.Create(fresh).GetActivitiesAsync(task.Id, 1)).Data;
        Assert.Equal(new[] { "TaskCreated", "TaskDetailsUpdated" }, history.Select(x => x.ActivityType));
    }
}
