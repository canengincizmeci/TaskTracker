using Microsoft.EntityFrameworkCore;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;
using TaskTracker.DataAccess.Concrete.EfCore;

namespace TaskTracker.Tests;

public class PostgreSqlMlDatasetTests
{
    [PostgreSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dataset_budgets_apply_only_to_authorized_scope(bool historyBudget)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var largeWorkspace = new Workspace
        {
            Name = "Large fixture",
            Members = [new() { UserId = 1, Role = WorkspaceRole.Owner, IsActive = true }]
        };
        context.Workspaces.Add(largeWorkspace);
        context.TaskRequests.Add(new TaskRequest
        {
            OwnerId = 2, Title = "Private personal fixture", Description = "Private body", Category = "Fixture",
            Activity = true,
            MlConsentGranted = true, MlConsentGrantedAt = DateTime.UtcNow,
            MlConsentPolicyVersion = TaskMlConsentEvent.CurrentPolicyVersion
        });
        await context.SaveChangesAsync();

        var taskCount = historyBudget ? 1 : EfTaskMlDatasetDal.MaxTasks + 1;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TaskRequests" ("Title", "Description", "Category", "Priority", "Status", "Visibility",
                "WorkspaceId", "OwnerId", "SharedCount", "MlConsentGranted", "MlConsentGrantedAt", "MlConsentPolicyVersion")
            SELECT 'Private workspace fixture', 'Private workspace body', 'Fixture', 'Medium', 'Pending', 0,
                {largeWorkspace.Id}, 1, 0, TRUE, CURRENT_TIMESTAMP, {TaskMlConsentEvent.CurrentPolicyVersion}
            FROM generate_series(1, {taskCount})
            """);
        if (historyBudget)
        {
            var taskId = await context.TaskRequests.Where(x => x.WorkspaceId == largeWorkspace.Id).Select(x => x.Id).SingleAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TaskActivities" ("TaskRequestId", "ActorUserId", "ActivityType")
                SELECT {taskId}, 1, 'TaskReopened' FROM generate_series(1, {EfTaskMlDatasetDal.MaxHistoryRecords + 1})
                """);
        }

        var dataset = TaskCollaborationTestServices.MlDatasets(context);
        Assert.Equal(TaskMlDatasetManager.DatasetLimitExceeded, (await dataset.ExportCsvAsync(1, largeWorkspace.Id)).Message);
        Assert.Equal(TaskMlDatasetManager.DatasetLimitExceeded, (await dataset.AnalyzeEligibilityAsync(1, largeWorkspace.Id)).Message);
        Assert.False((await dataset.ExportCsvAsync(2, largeWorkspace.Id)).Success);
        Assert.False((await dataset.AnalyzeEligibilityAsync(2, largeWorkspace.Id)).Success);

        var export = await dataset.ExportCsvAsync(2);
        Assert.True(export.Success);
        var row = Assert.Single(export.Data.Rows);
        Assert.Equal(row.TaskId, Assert.Single((await dataset.AnalyzeEligibilityAsync(2)).Data).TaskId);
        Assert.DoesNotContain("Private personal fixture", export.Data.CsvContent);
        Assert.DoesNotContain("Private body", export.Data.CsvContent);
        Assert.DoesNotContain("pg-user", export.Data.CsvContent);
    }

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
                MlConsentGranted = true,
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
