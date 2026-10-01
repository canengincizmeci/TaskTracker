using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class TaskPlanningRevisionTests
{
    [Fact]
    public async Task Personal_creation_writes_exact_initial_revision_with_creation_timestamp()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var rolloutCutoff = DateTime.UtcNow.AddMinutes(-1);

        var result = await TaskCollaborationTestServices.TaskRequests(context).AddTaskRequestAsync(new()
        {
            Title = "Initial title", Description = "Initial description", Category = "Course",
            Priority = TaskPriority.High, DueDate = dueDate
        }, 1);

        Assert.True(result.Success);
        var task = await context.TaskRequests.SingleAsync();
        var revision = await context.TaskPlanningRevisions.SingleAsync();
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(1, revision.ChangedByUserId);
        Assert.Equal(task.CreatedAt, revision.ChangedAt);
        Assert.True(IsCreationSnapshot(task, revision, rolloutCutoff));
        AssertSnapshot(revision, "Initial title", "Initial description", "Course", TaskPriority.High, dueDate);
    }

    [Fact]
    public async Task Workspace_creation_writes_exact_initial_revision()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var workspace = new Workspace
        {
            Name = "ML course",
            Members = [new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner }]
        };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

        var result = await TaskCollaborationTestServices.TaskRequests(context).AddWorkspaceTaskAsync(workspace.Id,
            new WorkspaceTaskCreateDto
            {
                Title = "Workspace title", Description = "Workspace description", Category = "Team",
                Priority = TaskPriority.Critical, DueDate = dueDate
            }, 1);

        Assert.True(result.Success);
        var task = await context.TaskRequests.SingleAsync();
        var revision = await context.TaskPlanningRevisions.SingleAsync();
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(1, revision.ChangedByUserId);
        Assert.Equal(task.CreatedAt, revision.ChangedAt);
        AssertSnapshot(revision, "Workspace title", "Workspace description", "Team", TaskPriority.Critical,
            dueDate);
    }

    [Fact]
    public async Task Planning_updates_append_full_snapshots_and_no_op_appends_nothing()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var manager = TaskCollaborationTestServices.TaskRequests(context);
        var initialDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        Assert.True((await manager.AddTaskRequestAsync(new()
        {
            Title = "Initial title", Description = "Initial description", Category = "Initial category",
            Priority = TaskPriority.Medium, DueDate = initialDueDate
        }, 1)).Success);

        Assert.True((await manager.UpdateTask(new UpdateTaskRequestDto
        {
            Id = 1, Version = 0, Title = "Updated title", Description = "Updated description",
            Category = "Updated category", Priority = TaskPriority.High, DueDate = initialDueDate.AddDays(2)
        }, 1)).Success);
        Assert.True((await manager.UpdateTask(new UpdateTaskRequestDto
        {
            Id = 1, Version = 1, Title = "Updated title", Description = "Updated description",
            Category = "Updated category", Priority = TaskPriority.Critical, DueDate = initialDueDate.AddDays(2)
        }, 1)).Success);

        var unchanged = new UpdateTaskRequestDto
        {
            Id = 1, Version = 2, Title = "Updated title", Description = "Updated description",
            Category = "Updated category", Priority = TaskPriority.Critical, DueDate = initialDueDate.AddDays(2)
        };
        Assert.True((await manager.UpdateTask(unchanged, 1)).Success);

        var revisions = await new TaskTracker.DataAccess.Concrete.EfCore.EfTaskRequestDal(context)
            .GetPlanningRevisionsAsync(1);
        Assert.Equal([1, 2, 3], revisions.Select(x => x.RevisionNumber));
        AssertSnapshot(revisions[0], "Initial title", "Initial description", "Initial category",
            TaskPriority.Medium, initialDueDate);
        AssertSnapshot(revisions[1], "Updated title", "Updated description", "Updated category",
            TaskPriority.High, initialDueDate.AddDays(2));
        AssertSnapshot(revisions[2], "Updated title", "Updated description", "Updated category",
            TaskPriority.Critical, initialDueDate.AddDays(2));
        Assert.Equal(2, await context.TaskActivities.CountAsync(x =>
            x.ActivityType == TaskActivityType.TaskDetailsUpdated));
    }

    [Fact]
    public async Task Stale_planning_edit_rolls_back_revision_and_activity()
    {
        using var database = new TestDatabase();
        using var first = database.CreateContext();
        var firstManager = TaskCollaborationTestServices.TaskRequests(first);
        Assert.True((await firstManager.AddTaskRequestAsync(CreateTask(), 1)).Success);
        using var stale = database.CreateContext();
        await stale.TaskRequests.SingleAsync();

        Assert.True((await firstManager.UpdateTask(Update(0, "Winning title"), 1)).Success);
        var staleResult = await TaskCollaborationTestServices.TaskRequests(stale)
            .UpdateTask(Update(0, "Losing title"), 1);

        Assert.False(staleResult.Success);
        using var verify = database.CreateContext();
        Assert.Equal("Winning title", (await verify.TaskRequests.SingleAsync()).Title);
        Assert.Equal(2, await verify.TaskPlanningRevisions.CountAsync());
        Assert.Single(await verify.TaskActivities.Where(x =>
            x.ActivityType == TaskActivityType.TaskDetailsUpdated).ToListAsync());
    }

    [Fact]
    public async Task Planning_revision_number_is_unique_per_task()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskPlanningRevisions.Add(Revision(1));
        await context.SaveChangesAsync();

        using var duplicate = database.CreateContext();
        duplicate.TaskPlanningRevisions.Add(Revision(1));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Fact]
    public async Task Planning_revision_unique_failure_maps_to_task_concurrency()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var manager = TaskCollaborationTestServices.TaskRequests(context);
        Assert.True((await manager.AddTaskRequestAsync(CreateTask(), 1)).Success);
        context.TaskPlanningRevisions.Add(Revision(2));

        var result = await manager.UpdateTask(Update(0, "Updated title"), 1);

        Assert.IsAssignableFrom<IConflictResult>(result);
        using var verify = database.CreateContext();
        Assert.Equal("Initial title", (await verify.TaskRequests.SingleAsync()).Title);
        Assert.Single(await verify.TaskPlanningRevisions.ToListAsync());
        Assert.Empty(await verify.TaskActivities.Where(x =>
            x.ActivityType == TaskActivityType.TaskDetailsUpdated).ToListAsync());
    }

    [Fact]
    public async Task Unrelated_unique_failure_is_not_mapped_to_planning_concurrency()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var manager = TaskCollaborationTestServices.TaskRequests(context);
        Assert.True((await manager.AddTaskRequestAsync(CreateTask(), 1)).Success);
        context.Users.Add(new User
        {
            FirstName = "Duplicate", LastName = "Email", UserName = "unique-user",
            NormalizedUserName = "unique-user", Email = "user1@example.test",
            PasswordHash = [1], PasswordSalt = [1], Status = true
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => manager.UpdateTask(Update(0, "Updated title"), 1));

        using var verify = database.CreateContext();
        Assert.Equal("Initial title", (await verify.TaskRequests.SingleAsync()).Title);
        Assert.Single(await verify.TaskPlanningRevisions.ToListAsync());
        Assert.Empty(await verify.TaskActivities.Where(x =>
            x.ActivityType == TaskActivityType.TaskDetailsUpdated).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Planning_revisions_cannot_be_modified_or_deleted(bool delete)
    {
        using var database = new TestDatabase();
        using (var seed = database.CreateContext())
        {
            seed.TaskRequests.Add(TestDatabase.Task());
            seed.TaskPlanningRevisions.Add(Revision(1));
            await seed.SaveChangesAsync();
        }

        using var mutation = database.CreateContext();
        var revision = await mutation.TaskPlanningRevisions.SingleAsync();
        if (delete)
            mutation.TaskPlanningRevisions.Remove(revision);
        else
            mutation.Entry(revision).Property(x => x.Title).CurrentValue = "Mutated title";

        await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.SaveChangesAsync());
    }

    [Fact]
    public async Task Legacy_task_first_update_is_not_a_creation_snapshot()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var createdAt = DateTime.UtcNow.AddDays(-30);
        var rolloutCutoff = createdAt.AddDays(1);
        var task = TestDatabase.Task();
        task.CreatedAt = createdAt;
        context.TaskRequests.Add(task);
        await context.SaveChangesAsync();

        Assert.Empty(await context.TaskPlanningRevisions.ToListAsync());
        Assert.True((await TaskCollaborationTestServices.TaskRequests(context)
            .UpdateTask(Update(0, "Legacy update"), 1)).Success);

        var revision = await context.TaskPlanningRevisions.SingleAsync();
        Assert.Equal(1, revision.RevisionNumber);
        Assert.NotEqual(task.CreatedAt, revision.ChangedAt);
        Assert.False(IsCreationSnapshot(task, revision, rolloutCutoff));
    }

    private static TaskRequestCreateDto CreateTask() => new()
    {
        Title = "Initial title", Description = "Initial description", Category = "Course",
        Priority = TaskPriority.Medium, DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3))
    };

    private static UpdateTaskRequestDto Update(long version, string title) => new()
    {
        Id = 1, Version = version, Title = title, Description = "Initial description", Category = "Course",
        Priority = TaskPriority.Medium, DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3))
    };

    private static TaskPlanningRevision Revision(int revisionNumber) => new()
    {
        TaskRequestId = 1, RevisionNumber = revisionNumber, ChangedByUserId = 1,
        Title = "Snapshot", Description = "Snapshot description", Category = "Tests",
        Priority = TaskPriority.Medium
    };

    private static void AssertSnapshot(TaskPlanningRevision revision, string title, string description,
        string category, TaskPriority priority, DateOnly? dueDate)
    {
        Assert.Equal(title, revision.Title);
        Assert.Equal(description, revision.Description);
        Assert.Equal(category, revision.Category);
        Assert.Equal(priority, revision.Priority);
        Assert.Equal(dueDate, revision.DueDate);
    }

    private static bool IsCreationSnapshot(TaskRequest task, TaskPlanningRevision revision,
        DateTime rolloutCutoff) =>
        task.CreatedAt >= rolloutCutoff && revision.RevisionNumber == 1 && revision.ChangedAt == task.CreatedAt;
}
