using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public class PostgreSqlTaskMlConsentTests
{
    [PostgreSqlFact]
    public async Task Existing_tasks_default_to_no_consent_after_migration()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("20261126153635_HashRefreshTokenCredentials");
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "TaskRequests" ("Title", "Description", "Category", "Priority", "Status", "Visibility",
                "OwnerId", "SharedCount")
            VALUES ('Legacy task', 'Created before consent', 'Fixture', 'Medium', 'Pending', 0, 1, 0)
            """);
        await context.Database.MigrateAsync();

        var task = await context.TaskRequests.SingleAsync();
        Assert.False(task.MlConsentGranted);
        Assert.Null(task.MlConsentGrantedAt);
        Assert.Null(task.MlConsentRevokedAt);
        Assert.Null(task.MlConsentPolicyVersion);
        Assert.Empty(await context.TaskMlConsentEvents.ToListAsync());
        Assert.Empty((await TaskCollaborationTestServices.MlDatasets(context).ExportCsvAsync(1)).Data.Rows);
    }

    [PostgreSqlFact]
    public async Task Opt_in_is_persisted_and_missing_or_revoked_consent_never_exports()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            var service = TaskCollaborationTestServices.TaskRequests(context);
            Assert.True((await service.AddTaskRequestAsync(new TaskRequestCreateDto
            {
                Title = "No consent", Description = "The request omitted the consent field", Category = "Fixture"
            }, 1)).Success);
            Assert.True((await service.AddTaskRequestAsync(new TaskRequestCreateDto
            {
                Title = "Explicit consent", Description = "The owner opted in", Category = "Fixture",
                MlConsentGranted = true
            }, 1)).Success);
        }

        await using (var context = database.CreateContext())
        {
            var tasks = await context.TaskRequests.OrderBy(x => x.Id).ToListAsync();
            Assert.False(tasks[0].MlConsentGranted);
            Assert.Null(tasks[0].MlConsentGrantedAt);
            Assert.True(tasks[1].MlConsentGranted);
            Assert.Equal(DateTimeKind.Utc, tasks[1].MlConsentGrantedAt!.Value.Kind);
            var audit = await context.TaskMlConsentEvents.SingleAsync();
            Assert.Equal(tasks[1].Id, audit.TaskRequestId);
            Assert.Equal(1, audit.ActorUserId);
            Assert.True(audit.Granted);
            Assert.Equal(tasks[1].MlConsentGrantedAt, audit.OccurredAtUtc);
            Assert.Equal(TaskMlConsentEvent.CurrentPolicyVersion, audit.PolicyVersion);

            var dataset = TaskCollaborationTestServices.MlDatasets(context);
            var export = await dataset.ExportCsvAsync(1);
            Assert.Equal(tasks[1].Id, Assert.Single(export.Data.Rows).TaskId);
            Assert.Equal("1", export.Data.CsvContent.Split('\n')[1].Split(',')[1]);
            Assert.Equal(tasks[1].Id, Assert.Single((await dataset.AnalyzeEligibilityAsync(1)).Data).TaskId);
            var service = TaskCollaborationTestServices.TaskRequests(context);
            Assert.True((await service.CompleteTaskAsync(tasks[1].Id,
                new TaskWorkflowCommandDto { Version = tasks[1].Version }, 1)).Success);
            Assert.True((await service.UpdateMlConsentAsync(tasks[1].Id,
                new UpdateTaskMlConsentDto { Granted = false, Version = tasks[1].Version }, 1)).Success);
        }

        await using (var verify = database.CreateContext())
        {
            var task = await verify.TaskRequests.OrderBy(x => x.Id).LastAsync();
            Assert.False(task.MlConsentGranted);
            var events = await verify.TaskMlConsentEvents.OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(new[] { true, false }, events.Select(x => x.Granted));
            Assert.Equal(events[0].OccurredAtUtc, task.MlConsentGrantedAt);
            Assert.Equal(events[1].OccurredAtUtc, task.MlConsentRevokedAt);
            Assert.Equal(events[0].PolicyVersion, events[1].PolicyVersion);
            var dataset = TaskCollaborationTestServices.MlDatasets(verify);
            Assert.Empty((await dataset.ExportCsvAsync(1)).Data.Rows);
            Assert.Empty((await dataset.ExportCsvAsync(1, eligibleOnly: true)).Data.Rows);
            Assert.Empty((await dataset.AnalyzeEligibilityAsync(1)).Data);

            var error = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlRawAsync(
                """UPDATE "TaskMlConsentEvents" SET "Granted" = FALSE"""));
            Assert.Equal("55000", error.SqlState);
        }
    }

    [PostgreSqlFact]
    public async Task Only_active_task_owner_can_write_consent_and_stale_writes_cannot_duplicate_history()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        int taskId;
        await using (var context = database.CreateContext())
        {
            var workspace = new Workspace
            {
                Name = "Consent workspace",
                Members = [
                    new() { UserId = 1, Role = WorkspaceRole.Owner, IsActive = true },
                    new() { UserId = 2, Role = WorkspaceRole.Admin, IsActive = true }
                ]
            };
            context.Workspaces.Add(workspace);
            await context.SaveChangesAsync();
            var service = TaskCollaborationTestServices.TaskRequests(context);
            var result = await service.AddWorkspaceTaskAsync(workspace.Id, new WorkspaceTaskCreateDto
            {
                Title = "Workspace task", Description = "Created with owner consent", Category = "Fixture",
                MlConsentGranted = true
            }, 2);
            Assert.True(result.Success);
            taskId = result.Data.Id!.Value;
            Assert.True(result.Data.MlConsent!.Granted);
            Assert.Null((await service.GetTaskById(taskId, 1)).Data.MlConsent);

            var revoke = new UpdateTaskMlConsentDto { Granted = false, Version = 0 };
            Assert.False((await service.UpdateMlConsentAsync(taskId, revoke, 1)).Success);
            Assert.False((await service.UpdateMlConsentAsync(taskId, revoke, 3)).Success);
            Assert.False((await service.UpdateMlConsentAsync(taskId, new() { Version = 0 }, 2)).Success);
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var command = new UpdateTaskMlConsentDto { Granted = false, Version = 0 };
        var results = await Task.WhenAll(
            TaskCollaborationTestServices.TaskRequests(first).UpdateMlConsentAsync(taskId, command, 2),
            TaskCollaborationTestServices.TaskRequests(second).UpdateMlConsentAsync(taskId, command, 2));
        Assert.Single(results, x => x.Success);
        Assert.Single(results, x => x is IConflictResult);

        await using var verify = database.CreateContext();
        var serviceAfterRace = TaskCollaborationTestServices.TaskRequests(verify);
        var task = await verify.TaskRequests.SingleAsync();
        Assert.Equal(1, task.Version);
        Assert.True((await serviceAfterRace.UpdateMlConsentAsync(taskId, new() { Granted = false, Version = 1 }, 2)).Success);
        Assert.Equal(2, await verify.TaskMlConsentEvents.CountAsync());
        Assert.True((await serviceAfterRace.UpdateMlConsentAsync(taskId, new() { Granted = true, Version = 1 }, 2)).Success);
        Assert.True((await serviceAfterRace.CompleteTaskAsync(taskId,
            new TaskWorkflowCommandDto { Version = 2 }, 2)).Success);
        var dataset = TaskCollaborationTestServices.MlDatasets(verify);
        Assert.Single((await dataset.ExportCsvAsync(1)).Data.Rows);
        var workspaceMember = await verify.WorkspaceMembers.SingleAsync(x => x.UserId == 2);
        var workspaceService = TaskCollaborationTestServices.Workspaces(verify, 1);
        Assert.True((await workspaceService.RemoveMemberAsync(workspaceMember.WorkspaceId, 2,
            new WorkspaceVersionDto { Version = workspaceMember.Version })).Success);
        Assert.False((await serviceAfterRace.UpdateMlConsentAsync(taskId, new() { Granted = false, Version = 3 }, 2)).Success);
        Assert.True((await verify.TaskRequests.AsNoTracking().SingleAsync()).MlConsentGranted);
        Assert.Empty((await dataset.ExportCsvAsync(1)).Data.Rows);
    }

    [PostgreSqlTheory]
    [InlineData("comment")]
    [InlineData("file")]
    [InlineData("edit")]
    public async Task Owner_consent_does_not_cover_independent_collaborator_contributions(string contribution)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var service = TaskCollaborationTestServices.TaskRequests(context);
        Assert.True((await service.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "Shared task", Description = "Only the owner granted consent", Category = "Fixture",
            MlConsentGranted = true
        }, 1)).Success);
        var task = await context.TaskRequests.SingleAsync();
        context.TaskShares.Add(new TaskShare
        {
            TaskRequestId = task.Id, SharedWithUserId = 2, Permission = TaskPermission.Edit
        });
        await context.SaveChangesAsync();

        var dataset = TaskCollaborationTestServices.MlDatasets(context);
        Assert.Single((await dataset.ExportCsvAsync(2)).Data.Rows);
        Assert.Empty((await dataset.ExportCsvAsync(3)).Data.Rows);
        switch (contribution)
        {
            case "comment":
                Assert.True((await TaskCollaborationTestServices.Create(context)
                    .SendMessageAsync(task.Id, 2, "COLLABORATOR_COMMENT")).Success);
                break;
            case "file":
                context.TaskAttachments.Add(new TaskAttachment
                {
                    TaskRequestId = task.Id, UploaderUserId = 2, Kind = TaskAttachmentKind.Task,
                    OriginalFileName = "collaborator.txt", StorageKey = Guid.NewGuid().ToString("N"),
                    ContentType = "text/plain", SizeBytes = 5
                });
                await context.SaveChangesAsync();
                break;
            case "edit":
                Assert.True((await service.UpdateTask(new UpdateTaskRequestDto
                {
                    Id = task.Id, Version = task.Version, Title = "Collaborator title",
                    Description = "Collaborator description", Category = "Fixture",
                    Priority = TaskPriority.Medium
                }, 2)).Success);
                break;
        }

        Assert.Empty((await dataset.ExportCsvAsync(1)).Data.Rows);
        Assert.Empty((await dataset.ExportCsvAsync(2)).Data.Rows);
        Assert.Empty((await dataset.AnalyzeEligibilityAsync(1)).Data);
    }
}
