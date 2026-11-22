using Microsoft.EntityFrameworkCore;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using static TaskTracker.Tests.SubTaskWorkflowTests;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class SubTaskAccessRevocationTests
{
    [Theory]
    [InlineData("downgrade")]
    [InlineData("remove")]
    [InlineData("workspace")]
    public async Task In_review_responsibility_blocks_revocation_then_changes_requested_allows_atomic_reset(string operation)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        int? workspaceId = null;
        if (operation == "workspace") workspaceId = await SeedWorkspaceAsync(context);
        else await SeedPersonalAsync(context);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var pending = Data(await service.CreateAsync(1, Details("Pending responsibility"), 1));
        pending = Data(await service.AssignAsync(1, pending.Id, new() { Version = pending.Version, AssigneeUserId = 2 }, 1));
        var reviewing = await StartDelegatedAsync(service, 2, "Review responsibility");
        reviewing = Data(await service.SubmitAsync(1, reviewing.Id,
            new() { Version = reviewing.Version, Content = "Reviewed work" }, 2));
        var submissionId = reviewing.CurrentSubmissionId!.Value;
        var activities = await context.SubTaskActivities.CountAsync();

        Task<IResult> Revoke() => operation switch
        {
            "downgrade" => TaskCollaborationTestServices.TaskShares(context, 1)
                .UpdateParticipantPermissionAsync(1, 2, new() { Permission = TaskPermission.View, Version = 0 }),
            "remove" => TaskCollaborationTestServices.TaskShares(context, 1).RemoveParticipantAsync(1, 2, 0),
            _ => TaskCollaborationTestServices.Workspaces(context, 1).RemoveMemberAsync(workspaceId!.Value, 2, new() { Version = 0 })
        };

        Assert.IsAssignableFrom<IConflictResult>(await Revoke());
        Assert.Equal(2, Data(await service.GetAsync(1, pending.Id, 2)).AssigneeUserId);
        Assert.Equal(activities, await context.SubTaskActivities.CountAsync());
        reviewing = Data(await service.ReviewAsync(1, reviewing.Id, submissionId,
            Review(reviewing.Version, TaskReviewDecision.ChangesRequested, "Revise"), 1));
        Assert.True((await Revoke()).Success);

        var subtasks = Data(await service.ListAsync(1, 1));
        Assert.All(subtasks, subTask =>
        {
            Assert.Null(subTask.AssigneeUserId);
            Assert.Equal(TaskStatus.Pending, subTask.Status);
        });
        var resetActivities = Data(await service.GetActivityHistoryAsync(1, reviewing.Id, 1));
        Assert.Single(resetActivities, x => x.ActivityType == TaskActivityType.UserUnassigned && x.TargetUserId == 2);
        Assert.Single(resetActivities, x => x.ActivityType == TaskActivityType.ResponsibilityReset);
        Assert.Equal(submissionId, Assert.Single(Data(await service.GetSubmissionHistoryAsync(1, reviewing.Id, 1))).Id);
        if (operation == "downgrade")
        {
            Assert.True((await service.GetSubmissionHistoryAsync(1, reviewing.Id, 2)).Success);
            Assert.Equal(Messages.AuthorizationDenied,
                (await service.StartAsync(1, reviewing.Id, Version(subtasks.Single(x => x.Id == reviewing.Id).Version), 2)).Message);
        }
        else
        {
            Assert.Equal(Messages.DataNotFound, (await service.GetAsync(1, pending.Id, 2)).Message);
            Assert.Equal(Messages.DataNotFound, (await service.GetSubmissionHistoryAsync(1, reviewing.Id, 2)).Message);
            Assert.Equal(Messages.DataNotFound, (await service.GetActivityHistoryAsync(1, reviewing.Id, 2)).Message);
        }
    }

    [Fact]
    public async Task Workspace_requires_active_owner_for_management_and_active_members_for_assignment()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await SeedWorkspaceAsync(context);
        var service = TaskCollaborationTestServices.SubTasks(context);
        var subTask = Data(await service.CreateAsync(1, Details(), 1));
        Assert.IsAssignableFrom<IConflictResult>(await service.AssignAsync(1, subTask.Id, new()
            { Version = subTask.Version, AssigneeUserId = 3 }, 1));
        Assert.Equal(Messages.AuthorizationDenied, (await service.CreateAsync(1, Details(), 2)).Message);
        var parent = await context.TaskRequests.SingleAsync();
        parent.OwnerId = 2;
        var member = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 2);
        member.IsActive = false;
        member.RemovedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        Assert.Equal(Messages.DataNotFound, (await service.CreateAsync(1, Details(), 2)).Message);
        Assert.Equal(Messages.AuthorizationDenied, (await service.CancelAsync(1, subTask.Id, Version(subTask.Version), 1)).Message);
        Assert.True((await service.ListAsync(1, 1)).Success);
    }
}
