using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using static TaskTracker.Tests.SubTaskWorkflowTests;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class SubTaskFrontendContractTests
{
    [Fact]
    public async Task Detail_exposes_capabilities_for_the_current_actor_and_actual_review_revision()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedPersonalAsync(context, TaskPermission.View);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var item = await StartDelegatedAsync(service, 2, "Delegated subtask");
        var owner = Data(await service.GetAsync(1, item.Id, 1));
        Assert.Equal("user2", owner.AssigneeUserName);
        Assert.True(owner.Capabilities!.CanAssign);
        Assert.False(owner.Capabilities.CanComplete);
        var assignee = Data(await service.GetAsync(1, item.Id, 2)).Capabilities!;
        Assert.True(assignee.CanSubmit);
        Assert.False(assignee.CanCancel);
        var viewer = Data(await service.GetAsync(1, item.Id, 3)).Capabilities!;
        Assert.False(viewer.CanEdit || viewer.CanDelete || viewer.CanAssign || viewer.CanStart ||
            viewer.CanComplete || viewer.CanSubmit || viewer.CanReview || viewer.CanCancel || viewer.CanReopen);

        item = Data(await service.SubmitAsync(1, item.Id, new() { Version = item.Version, Content = "Evidence" }, 2));
        Assert.True(Data(await service.GetAsync(1, item.Id, 1)).Capabilities!.CanReview);
        Assert.False(Data(await service.GetAsync(1, item.Id, 2)).Capabilities!.CanReview);
        var history = Assert.Single(Data(await service.GetSubmissionHistoryAsync(1, item.Id, 1)));
        Assert.Equal("user2", history.SubmittedByUserName);
        Assert.Equal(item.CurrentSubmissionId, history.Id);
        Assert.Contains(Data(await service.GetActivityHistoryAsync(1, item.Id, 1)),
            entry => entry.ActivityType == TaskActivityType.UserAssigned && entry.ActorUserName == "user1" && entry.TargetUserName == "user2");

        var parent = await context.TaskRequests.SingleAsync();
        parent.Status = TaskStatus.InReview;
        await context.SaveChangesAsync();
        var readOnly = Data(await service.GetAsync(1, item.Id, 1)).Capabilities!;
        Assert.False(readOnly.CanReview || readOnly.CanCancel || readOnly.CanDelete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assigned_work_includes_only_eligible_open_subtasks_with_active_parents(bool workspace)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        if (workspace) await SeedWorkspaceAsync(context);
        else await SeedPersonalAsync(context);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var item = await StartDelegatedAsync(service, 2, "Discoverable subtask");
        var second = await StartDelegatedAsync(service, 2, "Cancelled assignment");
        Assert.True((await service.CancelAsync(1, second.Id, Version(second.Version), 1)).Success);

        var assigned = Assert.Single(Data(await service.ListAssignedAsync(2)));
        Assert.Equal(item.Id, assigned.Id);
        Assert.Equal("Workflow task", assigned.ParentTitle);
        Assert.Empty(Data(await service.ListAssignedAsync(1)));
        Assert.Empty(Data(await service.ListAssignedAsync(3)));
        var parent = await context.TaskRequests.SingleAsync();
        parent.Status = TaskStatus.Cancelled;
        await context.SaveChangesAsync();
        Assert.Empty(Data(await service.ListAssignedAsync(2)));

        parent.Status = TaskStatus.Pending;
        if (workspace)
        {
            var member = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 2);
            member.IsActive = false;
            member.RemovedAt = DateTime.UtcNow;
        }
        else
        {
            var share = await context.TaskShares.SingleAsync(x => x.SharedWithUserId == 2);
            share.Permission = TaskPermission.View;
        }
        await context.SaveChangesAsync();
        Assert.Empty(Data(await service.ListAssignedAsync(2)));
    }
}
