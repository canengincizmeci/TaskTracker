using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class PostgreSqlPlanningRevisionTests
{
    [PostgreSqlFact]
    public async Task Migration_creates_planning_history_with_unique_revision_numbers()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        context.TaskPlanningRevisions.Add(Revision());
        await context.SaveChangesAsync();

        await using var duplicate = database.CreateContext();
        duplicate.TaskPlanningRevisions.Add(Revision());
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task Exact_planning_constraint_maps_to_task_concurrency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var manager = TaskCollaborationTestServices.TaskRequests(context);
        Assert.True((await manager.AddTaskRequestAsync(CreateTask(), 1)).Success);
        context.TaskPlanningRevisions.Add(new TaskPlanningRevision
        {
            TaskRequestId = 1, RevisionNumber = 2, ChangedByUserId = 1,
            Title = "Pending duplicate", Description = "Pending duplicate description", Category = "Tests",
            Priority = TaskPriority.Medium
        });

        var result = await manager.UpdateTask(new UpdateTaskRequestDto
        {
            Id = 1, Version = 0, Title = "Updated title", Description = "Initial description",
            Category = "Course", Priority = TaskPriority.Medium
        }, 1);

        Assert.IsAssignableFrom<IConflictResult>(result);
        await using var verify = database.CreateContext();
        Assert.Equal("Initial title", (await verify.TaskRequests.SingleAsync()).Title);
        Assert.Single(await verify.TaskPlanningRevisions.ToListAsync());
    }

    private static TaskPlanningRevision Revision() => new()
    {
        TaskRequestId = 1, RevisionNumber = 1, ChangedByUserId = 1,
        Title = "Snapshot", Description = "Snapshot description", Category = "Tests",
        Priority = TaskPriority.Medium
    };

    private static TaskRequestCreateDto CreateTask() => new()
    {
        Title = "Initial title", Description = "Initial description", Category = "Course",
        Priority = TaskPriority.Medium
    };
}
