using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.DataAccess.EfCore.UnitOfWork;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Concrete.EfCore;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class WorkspaceTaskIntegrationTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public async Task Only_active_owner_or_admin_can_create_workspace_tasks(int creatorId, bool allowed)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await AddUser(context, 4);
        var workspaceId = await SeedWorkspace(context);

        var result = await Tasks(context).AddWorkspaceTaskAsync(workspaceId, CreateDto(), creatorId);

        Assert.Equal(allowed, result.Success);
        Assert.Equal(allowed ? 1 : 0, await context.TaskRequests.CountAsync());
        if (allowed)
        {
            var task = await context.TaskRequests.SingleAsync();
            Assert.Equal(workspaceId, task.WorkspaceId);
            Assert.Equal(creatorId, task.OwnerId);
            Assert.Single(await context.TaskActivities.Where(x =>
                x.ActivityType == TaskActivityType.TaskCreated).ToListAsync());
        }
    }

    [Fact]
    public async Task Creation_accepts_only_active_workspace_assignee_without_creating_task_share()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await AddUser(context, 4);
        var workspaceId = await SeedWorkspace(context);
        var member = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 3);
        member.IsActive = false;
        member.RemovedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var removed = await Tasks(context).AddWorkspaceTaskAsync(workspaceId, CreateDto(3), 1);
        var unrelated = await Tasks(context).AddWorkspaceTaskAsync(workspaceId, CreateDto(4), 1);
        member.IsActive = true;
        member.RemovedAt = null;
        await context.SaveChangesAsync();
        var active = await Tasks(context).AddWorkspaceTaskAsync(workspaceId, CreateDto(3), 1);

        Assert.IsAssignableFrom<IConflictResult>(removed);
        Assert.IsAssignableFrom<IConflictResult>(unrelated);
        Assert.True(active.Success);
        Assert.Equal(3, (await context.TaskRequests.SingleAsync()).AssigneeUserId);
        Assert.Empty(await context.TaskShares.ToListAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Active_owner_admin_and_member_can_list_workspace_tasks(int userId)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);
        var task = await SeedTask(context, workspaceId, ownerId: 1, assigneeId: 3);

        var result = await Tasks(context).GetWorkspaceTasksAsync(workspaceId, userId);

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!);
        Assert.Equal(task.Id, item.Id);
        Assert.Equal(workspaceId, item.WorkspaceId);
        Assert.Equal("Product", item.WorkspaceName);
        Assert.Equal("user1", item.OwnerUserName);
        Assert.Equal("user3", item.AssigneeUserName);
        Assert.True(item.CanView);
        Assert.Equal(userId == 1, item.IsOwner);
    }

    [Fact]
    public async Task Unrelated_user_cannot_list_workspace_tasks()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await AddUser(context, 4);
        var workspaceId = await SeedWorkspace(context);
        await SeedTask(context, workspaceId, ownerId: 1);

        var result = await Tasks(context).GetWorkspaceTasksAsync(workspaceId, 4);

        Assert.False(result.Success);
        Assert.Equal(WorkspaceMessages.NotFound, result.Message);
    }

    [Fact]
    public async Task Removed_member_cannot_list_workspace_tasks()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);
        await SeedTask(context, workspaceId, ownerId: 1);
        var membership = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 3);
        membership.IsActive = false;
        membership.RemovedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var result = await Tasks(context).GetWorkspaceTasksAsync(workspaceId, 3);

        Assert.False(result.Success);
        Assert.Equal(WorkspaceMessages.NotFound, result.Message);
    }

    [Fact]
    public async Task Workspace_task_list_returns_only_active_tasks_from_requested_workspace_newest_first()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);
        var otherWorkspace = new Workspace
        {
            Name = "Other",
            Members = [new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner }]
        };
        context.Workspaces.Add(otherWorkspace);
        await context.SaveChangesAsync();

        var now = DateTime.UtcNow;
        context.TaskRequests.AddRange(
            WorkspaceTask(10, workspaceId, "Older", now.AddMinutes(-2)),
            WorkspaceTask(11, workspaceId, "Newer", now.AddMinutes(-1)),
            WorkspaceTask(12, otherWorkspace.Id, "Other workspace", now),
            WorkspaceTask(13, null, "Personal", now),
            WorkspaceTask(14, workspaceId, "Deleted", now, activity: false));
        await context.SaveChangesAsync();

        var result = await Tasks(context).GetWorkspaceTasksAsync(workspaceId, 1);

        Assert.True(result.Success);
        Assert.Equal([11, 10], result.Data!.Select(x => x.Id));
        Assert.All(result.Data, item => Assert.Equal(workspaceId, item.WorkspaceId));
        Assert.DoesNotContain(result.Data, item => item.Title is "Personal" or "Other workspace" or "Deleted");
    }

    [Fact]
    public async Task Empty_workspace_returns_an_empty_task_list()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);

        var result = await Tasks(context).GetWorkspaceTasksAsync(workspaceId, 2);

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task Existing_personal_task_list_still_returns_personal_tasks_unchanged()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var personal = TestDatabase.Task();
        context.TaskRequests.Add(personal);
        await context.SaveChangesAsync();

        var result = await Tasks(context).GetTasksByUserId(1);

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!);
        Assert.Equal(personal.Id, item.Id);
        Assert.Null(item.WorkspaceId);
        Assert.Null(item.WorkspaceName);
        Assert.True(item.IsOwner);
        Assert.True(item.CanView);
        Assert.True(item.CanEdit);
        Assert.True(item.CanShare);
    }

    [Fact]
    public async Task Active_members_can_view_and_collaborate_but_removed_and_unrelated_users_cannot()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await AddUser(context, 4);
        var workspaceId = await SeedWorkspace(context);
        var task = await SeedTask(context, workspaceId, ownerId: 1, assigneeId: 3);
        var collaboration = TaskCollaborationTestServices.Create(context);

        foreach (var userId in new[] { 1, 2, 3 })
        {
            var detail = await Tasks(context).GetTaskById(task.Id, userId);
            Assert.True(detail.Success);
            Assert.Equal(workspaceId, detail.Data!.WorkspaceId);
            Assert.Equal("Product", detail.Data.WorkspaceName);
            Assert.True(await collaboration.CanAccessAsync(task.Id, userId));
        }
        var adminDetail = (await Tasks(context).GetTaskById(task.Id, 2)).Data!;
        Assert.False(adminDetail.CanEdit);
        Assert.False(adminDetail.CanManageResponsibility);
        Assert.False(adminDetail.CanReview);
        Assert.True((await Tasks(context).GetTaskById(task.Id, 3)).Data!.CanStart);
        Assert.False((await Tasks(context).GetTaskById(task.Id, 4)).Success);
        Assert.False(await collaboration.CanAccessAsync(task.Id, 4));

        var adminMembership = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 2);
        adminMembership.IsActive = false;
        adminMembership.RemovedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        Assert.False((await Tasks(context).GetTaskById(task.Id, 2)).Success);
        Assert.False(await collaboration.CanAccessAsync(task.Id, 2));
    }

    [Fact]
    public async Task Task_owner_assigns_only_active_workspace_members_without_task_share()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        await AddUser(context, 4);
        var workspaceId = await SeedWorkspace(context);
        var task = await SeedTask(context, workspaceId, ownerId: 1);

        var active = await Tasks(context).AssignTaskAsync(task.Id,
            new AssignTaskDto { AssigneeUserId = 3, Version = task.Version }, 1);
        Assert.True(active.Success);
        Assert.Equal(3, task.AssigneeUserId);
        Assert.Empty(await context.TaskShares.ToListAsync());

        var unrelated = await Tasks(context).AssignTaskAsync(task.Id,
            new AssignTaskDto { AssigneeUserId = 4, Version = task.Version }, 1);
        Assert.IsAssignableFrom<IConflictResult>(unrelated);

        var member = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 2);
        member.IsActive = false;
        member.RemovedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        var removed = await Tasks(context).AssignTaskAsync(task.Id,
            new AssignTaskDto { AssigneeUserId = 2, Version = task.Version }, 1);
        Assert.IsAssignableFrom<IConflictResult>(removed);
        Assert.Equal(3, task.AssigneeUserId);
    }

    [Fact]
    public async Task Workspace_task_rejects_direct_sharing_while_personal_sharing_still_works()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);
        var workspaceTask = await SeedTask(context, workspaceId, ownerId: 1);
        var personalTask = TestDatabase.Task(2);
        context.TaskRequests.Add(personalTask);
        await context.SaveChangesAsync();

        var workspaceInvite = await TaskCollaborationTestServices.TaskShares(context, 1).InviteUserToTask(
            new InviteUserToTaskDto
            {
                TaskRequestId = workspaceTask.Id, Username = "user3", Permission = TaskPermission.Edit
            });
        var personalInvite = await TaskCollaborationTestServices.TaskShares(context, 1).InviteUserToTask(
            new InviteUserToTaskDto
            {
                TaskRequestId = personalTask.Id, Username = "user3", Permission = TaskPermission.Edit
            });

        Assert.IsAssignableFrom<IConflictResult>(workspaceInvite);
        Assert.True(personalInvite.Success);
        Assert.Single(await context.TaskShareInvitations.ToListAsync());
        Assert.Equal(personalTask.Id, (await context.TaskShareInvitations.SingleAsync()).TaskRequestId);
    }

    [Fact]
    public async Task Workspace_assignee_completes_submission_review_revision_and_approval_flow()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);
        var task = await SeedTask(context, workspaceId, ownerId: 2, assigneeId: 3);
        var manager = Tasks(context);

        Assert.True((await manager.StartTaskAsync(task.Id, new() { Version = task.Version }, 3)).Success);
        Assert.True((await manager.GetTaskById(task.Id, 3)).Data!.CanSubmit);
        var first = await manager.SubmitAsync(task.Id, new() { Version = task.Version, Content = "First" }, 3);
        Assert.True(first.Success);
        Assert.True((await manager.GetTaskById(task.Id, 2)).Data!.CanReview);
        Assert.False((await manager.GetTaskById(task.Id, 1)).Data!.CanReview);
        Assert.False((await manager.ReviewAsync(task.Id, first.Data!.Id,
            new() { Version = task.Version, Decision = TaskReviewDecision.Approved }, 1)).Success);
        Assert.True((await manager.ReviewAsync(task.Id, first.Data.Id,
            new()
            {
                Version = task.Version, Decision = TaskReviewDecision.ChangesRequested, Feedback = "Revise"
            }, 2)).Success);
        var second = await manager.SubmitAsync(task.Id,
            new() { Version = task.Version, Content = "Second" }, 3);
        Assert.True(second.Success);
        Assert.True((await manager.ReviewAsync(task.Id, second.Data!.Id,
            new() { Version = task.Version, Decision = TaskReviewDecision.Approved }, 2)).Success);

        Assert.Equal(TaskStatus.Completed, task.Status);
        Assert.Equal(2, await context.TaskSubmissions.CountAsync());
        Assert.Equal(2, await context.TaskSubmissionReviews.CountAsync());
        Assert.Empty(await context.TaskShares.ToListAsync());
    }

    [Theory]
    [InlineData(TaskStatus.Pending, 2)]
    [InlineData(TaskStatus.InProgress, 3)]
    [InlineData(TaskStatus.InReview, 3)]
    public async Task Active_task_owner_or_assignee_blocks_member_removal(TaskStatus status, int targetUserId)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);
        await SeedTask(context, workspaceId, ownerId: 2, assigneeId: 3, status);
        var target = await context.WorkspaceMembers.SingleAsync(x => x.UserId == targetUserId);

        var result = await Workspace(context, 1).RemoveMemberAsync(workspaceId, targetUserId,
            new WorkspaceVersionDto { Version = target.Version });

        Assert.IsAssignableFrom<IConflictResult>(result);
        Assert.True(target.IsActive);
    }

    [Theory]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Cancelled)]
    public async Task Historical_terminal_tasks_do_not_block_member_removal(TaskStatus status)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);
        await SeedTask(context, workspaceId, ownerId: 1, assigneeId: 3, status);
        var target = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 3);

        var result = await Workspace(context, 1).RemoveMemberAsync(workspaceId, 3,
            new WorkspaceVersionDto { Version = target.Version });

        Assert.True(result.Success);
        Assert.False(target.IsActive);
    }

    [Fact]
    public async Task Reopen_rejects_a_removed_workspace_assignee()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspaceId = await SeedWorkspace(context);
        var task = await SeedTask(context, workspaceId, ownerId: 1, assigneeId: 3, TaskStatus.Completed);
        var target = await context.WorkspaceMembers.SingleAsync(x => x.UserId == 3);
        Assert.True((await Workspace(context, 1).RemoveMemberAsync(workspaceId, 3,
            new WorkspaceVersionDto { Version = target.Version })).Success);

        var result = await Tasks(context).ReopenTaskAsync(task.Id,
            new TaskWorkflowCommandDto { Version = task.Version }, 1);

        Assert.IsAssignableFrom<IConflictResult>(result);
        Assert.Equal(TaskStatus.Completed, task.Status);
    }

    [Fact]
    public async Task Personal_task_access_assignment_sharing_and_workflow_remain_unchanged()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var personal = TestDatabase.Task();
        context.TaskRequests.Add(personal);
        await context.SaveChangesAsync();
        context.TaskShares.Add(new TaskShare
        {
            TaskRequestId = personal.Id, SharedWithUserId = 2, Permission = TaskPermission.Edit
        });
        await context.SaveChangesAsync();
        var manager = Tasks(context);

        Assert.True((await manager.GetTaskById(personal.Id, 2)).Success);
        Assert.True((await manager.AssignTaskAsync(personal.Id,
            new AssignTaskDto { AssigneeUserId = 2, Version = personal.Version }, 1)).Success);
        Assert.True((await manager.StartTaskAsync(personal.Id,
            new TaskWorkflowCommandDto { Version = personal.Version }, 2)).Success);
        var submitted = await manager.SubmitAsync(personal.Id,
            new CreateTaskSubmissionDto { Version = personal.Version, Content = "Done" }, 2);
        Assert.True(submitted.Success);
        Assert.True((await manager.ReviewAsync(personal.Id, submitted.Data!.Id,
            new ReviewTaskSubmissionDto
            {
                Version = personal.Version, Decision = TaskReviewDecision.Approved
            }, 1)).Success);
        Assert.Null(personal.WorkspaceId);
        Assert.Equal(TaskStatus.Completed, personal.Status);
    }

    private static TaskRequestManager Tasks(TaskTrackerDbContext context) =>
        TaskCollaborationTestServices.TaskRequests(context);

    private static WorkspaceManager Workspace(TaskTrackerDbContext context, int userId)
    {
        var uow = new UnitOfWork(context);
        return new WorkspaceManager(uow, new EfWorkspaceDal(context), new CurrentUser(userId),
            TaskCollaborationTestServices.Create(context),
            NullLogger<WorkspaceManager>.Instance, new IdentityNormalizer());
    }

    private static WorkspaceTaskCreateDto CreateDto(int? assigneeId = null) => new()
    {
        Title = "Workspace task", Description = "Workspace task description", Category = "Tests",
        Priority = TaskPriority.Medium, Status = TaskStatus.Pending, AssigneeUserId = assigneeId
    };

    private static async Task<int> SeedWorkspace(TaskTrackerDbContext context)
    {
        var workspace = new Workspace
        {
            Name = "Product",
            Members =
            [
                new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner },
                new WorkspaceMember { UserId = 2, Role = WorkspaceRole.Admin },
                new WorkspaceMember { UserId = 3, Role = WorkspaceRole.Member }
            ]
        };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();
        return workspace.Id;
    }

    private static async Task<TaskRequest> SeedTask(TaskTrackerDbContext context, int workspaceId, int ownerId,
        int? assigneeId = null, TaskStatus status = TaskStatus.Pending)
    {
        var task = TestDatabase.Task();
        task.WorkspaceId = workspaceId;
        task.OwnerId = ownerId;
        task.AssigneeUserId = assigneeId;
        task.Status = status;
        context.TaskRequests.Add(task);
        await context.SaveChangesAsync();
        return task;
    }

    private static TaskRequest WorkspaceTask(int id, int? workspaceId, string title, DateTime createdAt,
        bool activity = true) => new()
    {
        Id = id,
        WorkspaceId = workspaceId,
        OwnerId = 1,
        Title = title,
        Description = $"{title} description",
        Category = "Tests",
        Priority = TaskPriority.Medium,
        Status = TaskStatus.Pending,
        Activity = activity,
        Visibility = TaskVisibility.Private,
        CreatedAt = createdAt
    };

    private static async Task AddUser(TaskTrackerDbContext context, int id)
    {
        context.Users.Add(new User
        {
            Id = id, FirstName = "Test", LastName = "User", UserName = $"user{id}", NormalizedUserName = $"user{id}",
            Email = $"user{id}@example.test", PasswordHash = [1], PasswordSalt = [1], Status = true
        });
        await context.SaveChangesAsync();
    }

    private sealed class CurrentUser(int id) : ICurrentUserService
    {
        public int UserId => id;
    }
}
