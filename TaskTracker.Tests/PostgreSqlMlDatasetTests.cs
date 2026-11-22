using Microsoft.EntityFrameworkCore;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class PostgreSqlMlDatasetTests
{
    [PostgreSqlFact]
    public async Task PostgreSql_migration_enforces_snapshot_constraints_and_round_trips_microsecond_deadlines()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        const int offsetMinutes = 180;

        await using (var context = database.CreateContext())
        {
            var manager = TaskCollaborationTestServices.TaskRequests(context);
            var created = await manager.AddTaskRequestAsync(new TaskRequestCreateDto
            {
                Title = "PostgreSQL ML snapshot",
                Description = "Verify migration constraints and timestamp precision",
                Category = "Database",
                Priority = TaskPriority.High,
                DueDate = dueDate,
                UtcOffsetMinutes = offsetMinutes
            }, 1);
            Assert.True(created.Success);
            Assert.True((await manager.CompleteTaskAsync(1, new TaskWorkflowCommandDto { Version = 0 }, 1)).Success);
        }

        await using (var verify = database.CreateContext())
        {
            var snapshot = await verify.TaskPlanningSnapshots.SingleAsync();
            Assert.Equal(TaskPlanningSnapshot.ComputeOriginalDueAtUtc(dueDate, offsetMinutes), snapshot.OriginalDueAtUtc);

            var export = await TaskCollaborationTestServices.MlDatasets(verify).ExportCsvAsync(1);
            Assert.True(export.Success);
            var row = Assert.Single(export.Data.Rows);
            Assert.True(row.IsEligible);
            Assert.False(row.IsLate);
        }

        await using var duplicate = database.CreateContext();
        duplicate.TaskPlanningSnapshots.Add(new TaskPlanningSnapshot
        {
            TaskRequestId = 1,
            InitialPriority = TaskPriority.Low,
            InitialAssignmentState = TaskInitialAssignmentState.Unassigned,
            InitialVisibility = TaskVisibility.Private,
            InitialTitleLength = 10,
            InitialDescriptionLength = 20
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task PostgreSql_concurrent_updates_preserve_single_initial_snapshot_and_atomic_revision_history()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var initialDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));

        await using (var seed = database.CreateContext())
        {
            var manager = TaskCollaborationTestServices.TaskRequests(seed);
            Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
            {
                Title = "Initial PG planning title",
                Description = "Initial PG planning description",
                Category = "ML",
                Priority = TaskPriority.Low,
                DueDate = initialDueDate,
                UtcOffsetMinutes = 0
            }, 1)).Success);
        }

        var barrier = new SaveBarrierInterceptor("INSERT INTO \"TaskPlanningRevisions\"");
        await using var first = database.CreateContext(barrier);
        await using var second = database.CreateContext(barrier);

        var firstUpdate = TaskCollaborationTestServices.TaskRequests(first).UpdateTask(new UpdateTaskRequestDto
        {
            Id = 1, Version = 0, Title = "First concurrent update",
            Description = "Initial PG planning description", Category = "ML",
            Priority = TaskPriority.High, DueDate = initialDueDate.AddDays(3)
        }, 1);

        var secondUpdate = TaskCollaborationTestServices.TaskRequests(second).UpdateTask(new UpdateTaskRequestDto
        {
            Id = 1, Version = 0, Title = "Second concurrent update",
            Description = "Initial PG planning description", Category = "ML",
            Priority = TaskPriority.Critical, DueDate = initialDueDate.AddDays(7)
        }, 1);

        var results = await Task.WhenAll(firstUpdate, secondUpdate);
        Assert.Single(results, r => r.Success);
        Assert.Single(results, r => r is IConflictResult);

        await using var verify = database.CreateContext();
        var snapshot = await verify.TaskPlanningSnapshots.SingleAsync();
        Assert.Equal(initialDueDate, snapshot.InitialDueDate);
        Assert.Equal(TaskPriority.Low, snapshot.InitialPriority);
        Assert.Equal(2, await verify.TaskPlanningRevisions.CountAsync());
        Assert.Single(await verify.TaskActivities.Where(x =>
            x.ActivityType == TaskActivityType.TaskDetailsUpdated).ToListAsync());
    }
}
