using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Migrations;
using TaskTracker.Core.Utilities.Enums;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class TaskDeletionAuditTests
{
    [Fact]
    public async Task Delete_records_actor_timestamp_and_audit_event_without_changing_status()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var task = TestDatabase.Task();
        task.Status = TaskStatus.InProgress;
        context.TaskRequests.Add(task);
        await context.SaveChangesAsync();

        var result = await TaskCollaborationTestServices.TaskRequests(context).DeleteTask(task.Id, 1);

        Assert.True(result.Success);
        Assert.False(task.Activity);
        Assert.NotNull(task.DeletedAt);
        Assert.Equal(1, task.DeletedByUserId);
        Assert.Equal(TaskStatus.InProgress, task.Status);
        var activity = await context.TaskActivities.SingleAsync();
        Assert.Equal(TaskActivityType.TaskDeleted, activity.ActivityType);
        Assert.Equal(1, activity.ActorUserId);
        Assert.Equal(task.Id, activity.TaskRequestId);
        Assert.Equal(task.DeletedAt, activity.CreatedAt);
        Assert.Null(activity.FromStatus);
        Assert.Null(activity.ToStatus);
        Assert.Empty(await context.TaskActivities.Where(x =>
            x.ActivityType == TaskActivityType.TaskCancelled ||
            x.ActivityType == TaskActivityType.ResponsibilityReset).ToListAsync());
        Assert.False((await TaskCollaborationTestServices.TaskRequests(context).GetTaskById(task.Id, 1)).Success);
        Assert.Empty((await TaskCollaborationTestServices.TaskRequests(context).GetTasksByUserId(1)).Data);
    }

    [Fact]
    public async Task Activity_insert_failure_rolls_back_deletion_metadata_and_state()
    {
        using var database = new TestDatabase();
        await using (var seed = database.CreateContext())
        {
            seed.TaskRequests.Add(TestDatabase.Task());
            await seed.SaveChangesAsync();
        }

        await using (var failing = database.CreateContext(new FailTaskDeletedInsertInterceptor()))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                TaskCollaborationTestServices.TaskRequests(failing).DeleteTask(1, 1));
        }

        await using var verify = database.CreateContext();
        var task = await verify.TaskRequests.SingleAsync();
        Assert.True(task.Activity);
        Assert.Null(task.DeletedAt);
        Assert.Null(task.DeletedByUserId);
        Assert.Empty(await verify.TaskActivities.ToListAsync());
    }

    [Fact]
    public async Task Stale_delete_rolls_back_its_activity_and_metadata()
    {
        using var database = new TestDatabase();
        await using (var seed = database.CreateContext())
        {
            seed.TaskRequests.Add(TestDatabase.Task());
            await seed.SaveChangesAsync();
        }

        await using var stale = database.CreateContext();
        await stale.TaskRequests.SingleAsync();
        await using (var winner = database.CreateContext())
        {
            var task = await winner.TaskRequests.SingleAsync();
            task.Title = "Concurrent winner";
            await winner.SaveChangesAsync();
        }

        var result = await TaskCollaborationTestServices.TaskRequests(stale).DeleteTask(1, 1);

        Assert.False(result.Success);
        await using var verify = database.CreateContext();
        var stored = await verify.TaskRequests.SingleAsync();
        Assert.True(stored.Activity);
        Assert.Null(stored.DeletedAt);
        Assert.Null(stored.DeletedByUserId);
        Assert.Equal("Concurrent winner", stored.Title);
        Assert.Empty(await verify.TaskActivities.ToListAsync());
    }

    [Fact]
    public async Task Repeated_delete_returns_not_found_without_overwriting_or_duplicating_history()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        await context.SaveChangesAsync();
        var manager = TaskCollaborationTestServices.TaskRequests(context);
        Assert.True((await manager.DeleteTask(1, 1)).Success);
        var originalDeletedAt = (await context.TaskRequests.SingleAsync()).DeletedAt;
        var originalDeletedBy = (await context.TaskRequests.SingleAsync()).DeletedByUserId;

        var repeated = await manager.DeleteTask(1, 1);

        Assert.False(repeated.Success);
        Assert.Equal(originalDeletedAt, (await context.TaskRequests.SingleAsync()).DeletedAt);
        Assert.Equal(originalDeletedBy, (await context.TaskRequests.SingleAsync()).DeletedByUserId);
        Assert.Single(await context.TaskActivities.Where(x =>
            x.ActivityType == TaskActivityType.TaskDeleted).ToListAsync());
    }

    [Fact]
    public async Task Created_and_deleted_timestamps_reconstruct_historical_activity()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var task = TestDatabase.Task();
        task.CreatedAt = DateTime.UtcNow.AddMinutes(-1);
        context.TaskRequests.Add(task);
        await context.SaveChangesAsync();
        Assert.True((await TaskCollaborationTestServices.TaskRequests(context).DeleteTask(task.Id, 1)).Success);

        var deletedAt = task.DeletedAt!.Value;
        Assert.True(WasActiveAt(task, deletedAt.AddTicks(-1)));
        Assert.False(WasActiveAt(task, deletedAt));
        Assert.False(WasActiveAt(task, deletedAt.AddTicks(1)));
        Assert.Equal(deletedAt, (await context.TaskActivities.SingleAsync(x =>
            x.ActivityType == TaskActivityType.TaskDeleted)).CreatedAt);
    }

    [Fact]
    public async Task Unauthorized_user_cannot_create_deletion_metadata_or_activity()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        context.TaskRequests.Add(TestDatabase.Task());
        await context.SaveChangesAsync();

        var result = await TaskCollaborationTestServices.TaskRequests(context).DeleteTask(1, 2);

        Assert.False(result.Success);
        var task = await context.TaskRequests.SingleAsync();
        Assert.True(task.Activity);
        Assert.Null(task.DeletedAt);
        Assert.Null(task.DeletedByUserId);
        Assert.Empty(await context.TaskActivities.ToListAsync());
    }

    [Fact]
    public async Task Legacy_inactive_rows_remain_distinct_from_fully_audited_deletions()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var legacy = TestDatabase.Task();
        legacy.Activity = false;
        context.TaskRequests.Add(legacy);
        await context.SaveChangesAsync();

        var result = await TaskCollaborationTestServices.TaskRequests(context).DeleteTask(legacy.Id, 1);

        Assert.False(result.Success);
        Assert.True(IsLegacyInactive(legacy));
        Assert.False(IsFullyAuditedDeletion(legacy));
        Assert.Null(legacy.DeletedAt);
        Assert.Null(legacy.DeletedByUserId);
        Assert.Empty(await context.TaskActivities.ToListAsync());
    }

    [Fact]
    public async Task Database_constraint_rejects_partial_deletion_provenance()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var task = TestDatabase.Task();
        task.Activity = false;
        task.DeletedAt = DateTime.UtcNow;
        context.TaskRequests.Add(task);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void Migration_adds_nullable_provenance_without_fabricating_legacy_values()
    {
        var migration = new AddTaskDeletionAudit
        {
            ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL"
        };

        Assert.DoesNotContain(migration.UpOperations,
            operation => operation is SqlOperation or UpdateDataOperation);
        var columns = migration.UpOperations.OfType<AddColumnOperation>().ToList();
        Assert.Equal(2, columns.Count);
        Assert.All(columns, column => Assert.True(column.IsNullable));
        Assert.Contains(columns, column => column.Name == nameof(TaskRequest.DeletedAt));
        Assert.Contains(columns, column => column.Name == nameof(TaskRequest.DeletedByUserId));
        var constraint = Assert.Single(migration.UpOperations.OfType<AddCheckConstraintOperation>());
        Assert.Contains("\"Activity\" = FALSE", constraint.Sql, StringComparison.Ordinal);
        Assert.Contains("\"DeletedAt\" IS NULL", constraint.Sql, StringComparison.Ordinal);
        Assert.Contains("\"DeletedByUserId\" IS NULL", constraint.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleting_user_relationship_is_optional_indexed_and_restricted()
    {
        using var database = new TestDatabase();
        using var context = database.CreateContext();
        var entity = context.Model.FindEntityType(typeof(TaskRequest))!;
        var property = entity.FindProperty(nameof(TaskRequest.DeletedByUserId))!;
        Assert.True(property.IsNullable);
        Assert.Contains(entity.GetIndexes(), index => index.Properties.SequenceEqual([property]));
        var foreignKey = Assert.Single(entity.GetForeignKeys(), key => key.Properties.SequenceEqual([property]));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_delete_round_trips_matching_timestamp_and_actor()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            context.TaskRequests.Add(TestDatabase.Task());
            await context.SaveChangesAsync();
            Assert.True((await TaskCollaborationTestServices.TaskRequests(context).DeleteTask(1, 1)).Success);
        }

        await using var verify = database.CreateContext();
        var task = await verify.TaskRequests.SingleAsync();
        var activity = await verify.TaskActivities.SingleAsync(x =>
            x.ActivityType == TaskActivityType.TaskDeleted);
        Assert.False(task.Activity);
        Assert.Equal(1, task.DeletedByUserId);
        Assert.Equal(task.DeletedAt, activity.CreatedAt);
    }

    private static bool WasActiveAt(TaskRequest task, DateTime timestamp) =>
        task.CreatedAt <= timestamp && (!task.DeletedAt.HasValue || task.DeletedAt > timestamp);

    private static bool IsLegacyInactive(TaskRequest task) =>
        !task.Activity && !task.DeletedAt.HasValue && !task.DeletedByUserId.HasValue;

    private static bool IsFullyAuditedDeletion(TaskRequest task) =>
        !task.Activity && task.DeletedAt.HasValue && task.DeletedByUserId.HasValue;

    private sealed class FailTaskDeletedInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            command.CommandText.Contains("INSERT INTO \"TaskActivities\"", StringComparison.OrdinalIgnoreCase)
                ? ValueTask.FromException<InterceptionResult<DbDataReader>>(
                    new DbUpdateException("Simulated TaskDeleted insert failure."))
                : ValueTask.FromResult(result);
    }
}
