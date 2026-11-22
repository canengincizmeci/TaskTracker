using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TaskTracker.API.Controllers;
using TaskTracker.Bussiness.Concrete;
using TaskTracker.Core.DataAccess;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Migrations;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Tests;

public class TaskMlDatasetTests
{
    private sealed class FixedCurrentUser(int userId) : TaskTracker.Bussiness.Abstract.ICurrentUserService
    {
        public int UserId => userId;
    }

    [Fact]
    public async Task Personal_and_workspace_creation_captures_exact_initial_planning_snapshot()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspace = new Workspace
        {
            Name = "ML Research Lab",
            Members =
            [
                new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner, IsActive = true },
                new WorkspaceMember { UserId = 2, Role = WorkspaceRole.Member, IsActive = true }
            ]
        };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();

        var manager = TaskCollaborationTestServices.TaskRequests(context);
        var personalDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        const int offsetMinutes = 180; // UTC+3

        var personalResult = await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "Personal ML task",
            Description = "Collect clean planning features",
            Category = "University",
            Priority = TaskPriority.High,
            DueDate = personalDueDate,
            UtcOffsetMinutes = offsetMinutes
        }, 1);
        Assert.True(personalResult.Success);

        var workspaceDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var delegatedResult = await manager.AddWorkspaceTaskAsync(workspace.Id, new WorkspaceTaskCreateDto
        {
            Title = "Delegated workspace task",
            Description = "Assigned to member two at creation time",
            Category = "Research",
            Priority = TaskPriority.Critical,
            DueDate = workspaceDueDate,
            UtcOffsetMinutes = -300,
            AssigneeUserId = 2
        }, 1);
        Assert.True(delegatedResult.Success);

        var selfAssignedResult = await manager.AddWorkspaceTaskAsync(workspace.Id, new WorkspaceTaskCreateDto
        {
            Title = "Self-assigned workspace task",
            Description = "Owner assigns self at creation without deadline",
            Category = "Research",
            Priority = TaskPriority.Low,
            DueDate = null,
            UtcOffsetMinutes = 0,
            AssigneeUserId = 1
        }, 1);
        Assert.True(selfAssignedResult.Success);

        var snapshots = await context.TaskPlanningSnapshots.OrderBy(x => x.TaskRequestId).ToListAsync();
        Assert.Equal(3, snapshots.Count);

        var personalTask = await context.TaskRequests.SingleAsync(x => x.Id == 1);
        var s1 = snapshots[0];
        Assert.Equal(personalTask.Id, s1.TaskRequestId);
        Assert.Equal(TaskPlanningSnapshot.CurrentSchemaVersion, s1.SchemaVersion);
        Assert.Equal(personalTask.CreatedAt, s1.CreatedAtUtc);
        Assert.Equal(offsetMinutes, s1.InitialUtcOffsetMinutes);
        Assert.Equal(TaskPriority.High, s1.InitialPriority);
        Assert.Equal(personalDueDate, s1.InitialDueDate);
        Assert.Equal(TaskPlanningSnapshot.ComputeOriginalDueAtUtc(personalDueDate, offsetMinutes), s1.OriginalDueAtUtc);
        Assert.NotNull(s1.InitialPlannedCalendarDays);
        Assert.True(s1.InitialPlannedCalendarDays >= 4);
        Assert.NotNull(s1.InitialPlannedDurationSeconds);
        Assert.True(s1.InitialPlannedDurationSeconds > 0);
        Assert.Equal(TaskInitialAssignmentState.Unassigned, s1.InitialAssignmentState);
        Assert.False(s1.IsWorkspaceTask);
        Assert.Equal(TaskVisibility.Private, s1.InitialVisibility);
        Assert.Equal("Personal ML task".Length, s1.InitialTitleLength);
        Assert.Equal("Collect clean planning features".Length, s1.InitialDescriptionLength);

        var s2 = snapshots[1];
        Assert.Equal(TaskInitialAssignmentState.Delegated, s2.InitialAssignmentState);
        Assert.True(s2.IsWorkspaceTask);
        Assert.Equal(TaskPriority.Critical, s2.InitialPriority);
        Assert.Equal(-300, s2.InitialUtcOffsetMinutes);
        Assert.Equal(TaskPlanningSnapshot.ComputeOriginalDueAtUtc(workspaceDueDate, -300), s2.OriginalDueAtUtc);

        var s3 = snapshots[2];
        Assert.Equal(TaskInitialAssignmentState.SelfAssigned, s3.InitialAssignmentState);
        Assert.True(s3.IsWorkspaceTask);
        Assert.Null(s3.InitialDueDate);
        Assert.Null(s3.OriginalDueAtUtc);
        Assert.Null(s3.InitialPlannedCalendarDays);
        Assert.Null(s3.InitialPlannedDurationSeconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Planning_snapshots_are_immutable_and_unique_per_task(bool delete)
    {
        using var database = new TestDatabase();
        await using (var seed = database.CreateContext())
        {
            var manager = TaskCollaborationTestServices.TaskRequests(seed);
            Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
            {
                Title = "Immutable snapshot",
                Description = "Snapshot cannot be edited or deleted",
                Category = "Tests",
                Priority = TaskPriority.Medium,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2))
            }, 1)).Success);
        }

        await using (var mutation = database.CreateContext())
        {
            var snapshot = await mutation.TaskPlanningSnapshots.SingleAsync();
            if (delete)
                mutation.TaskPlanningSnapshots.Remove(snapshot);
            else
                mutation.Entry(snapshot).Property(x => x.InitialTitleLength).CurrentValue = 99;

            await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.SaveChangesAsync());
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

    [Fact]
    public async Task Editing_deadline_and_priority_does_not_alter_initial_snapshot_values()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var manager = TaskCollaborationTestServices.TaskRequests(context);
        var initialDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));

        Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "Original short title",
            Description = "Original planning description",
            Category = "ML",
            Priority = TaskPriority.Low,
            DueDate = initialDueDate,
            UtcOffsetMinutes = 120
        }, 1)).Success);

        var editedDueDate = initialDueDate.AddDays(10);
        Assert.True((await manager.UpdateTask(new UpdateTaskRequestDto
        {
            Id = 1,
            Version = 0,
            Title = "Much longer edited title after replanning",
            Description = "Edited description with considerably more characters added later",
            Category = "Edited Category",
            Priority = TaskPriority.Critical,
            DueDate = editedDueDate,
            UtcOffsetMinutes = 120
        }, 1)).Success);

        Assert.True((await manager.UpdateTask(new UpdateTaskRequestDto
        {
            Id = 1,
            Version = 1,
            Title = "Removed deadline title",
            Description = "Edited description removing deadline entirely",
            Category = "Edited Category",
            Priority = TaskPriority.High,
            DueDate = null,
            UtcOffsetMinutes = 120
        }, 1)).Success);

        var task = await context.TaskRequests.SingleAsync();
        Assert.Null(task.DueDate);
        Assert.Equal(TaskPriority.High, task.Priority);

        var snapshot = await context.TaskPlanningSnapshots.SingleAsync();
        Assert.Equal(initialDueDate, snapshot.InitialDueDate);
        Assert.Equal(TaskPlanningSnapshot.ComputeOriginalDueAtUtc(initialDueDate, 120), snapshot.OriginalDueAtUtc);
        Assert.Equal(TaskPriority.Low, snapshot.InitialPriority);
        Assert.Equal("Original short title".Length, snapshot.InitialTitleLength);
        Assert.Equal("Original planning description".Length, snapshot.InitialDescriptionLength);

        var export = await TaskCollaborationTestServices.MlDatasets(context).ExportCsvAsync(1);
        Assert.True(export.Success);
        var row = Assert.Single(export.Data.Rows);
        Assert.Equal(initialDueDate, row.InitialDueDate);
        Assert.Equal(TaskPriority.Low, row.InitialPriority);
        Assert.Equal(0, row.InitialPriorityLevel);
    }

    [Fact]
    public async Task Completing_tasks_produces_exact_labels_against_original_deadline_even_when_deadline_was_edited()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspace = new Workspace
        {
            Name = "Labeling Workspace",
            Members =
            [
                new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner, IsActive = true },
                new WorkspaceMember { UserId = 2, Role = WorkspaceRole.Member, IsActive = true }
            ]
        };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();

        var baseCreatedUtc = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
        var originalDueDate = new DateOnly(2026, 11, 15);
        const int offsetMinutes = 180; // UTC+3 -> end of 2026-11-15 local is 2026-11-15T20:59:59.999999Z
        var originalDueAtUtc = TaskPlanningSnapshot.ComputeOriginalDueAtUtc(originalDueDate, offsetMinutes);
        Assert.Equal(new DateTime(2026, 11, 15, 20, 59, 59, 999, 999, DateTimeKind.Utc), originalDueAtUtc);

        // Task 1: Personal task completed on-time (30 seconds before original_due_at), even though current DueDate was later shortened to 2026-11-12.
        await SeedInstrumentedTaskAsync(
            context,
            taskId: 1,
            ownerId: 1,
            workspaceId: null,
            assigneeId: null,
            createdAtUtc: baseCreatedUtc,
            initialDueDate: originalDueDate,
            currentDueDate: new DateOnly(2026, 11, 12),
            offsetMinutes: offsetMinutes,
            status: TaskStatus.Completed,
            activities:
            [
                new TaskActivity
                {
                    TaskRequestId = 1, ActorUserId = 1, ActivityType = TaskActivityType.TaskCreated,
                    CreatedAt = baseCreatedUtc
                },
                new TaskActivity
                {
                    TaskRequestId = 1, ActorUserId = 1, ActivityType = TaskActivityType.TaskCompleted,
                    FromStatus = TaskStatus.Pending, ToStatus = TaskStatus.Completed,
                    CreatedAt = originalDueAtUtc.AddSeconds(-30)
                }
            ]);

        // Task 2: Personal task completed late (1 second after original_due_at), even though current DueDate was extended to 2026-11-25.
        await SeedInstrumentedTaskAsync(
            context,
            taskId: 2,
            ownerId: 1,
            workspaceId: null,
            assigneeId: null,
            createdAtUtc: baseCreatedUtc,
            initialDueDate: originalDueDate,
            currentDueDate: new DateOnly(2026, 11, 25),
            offsetMinutes: offsetMinutes,
            status: TaskStatus.Completed,
            activities:
            [
                new TaskActivity
                {
                    TaskRequestId = 2, ActorUserId = 1, ActivityType = TaskActivityType.TaskCreated,
                    CreatedAt = baseCreatedUtc
                },
                new TaskActivity
                {
                    TaskRequestId = 2, ActorUserId = 1, ActivityType = TaskActivityType.TaskCompleted,
                    FromStatus = TaskStatus.InProgress, ToStatus = TaskStatus.Completed,
                    CreatedAt = originalDueAtUtc.AddSeconds(1)
                }
            ]);

        // Task 3: Delegated workspace task submitted before deadline, changes requested, and approved after original_due_at -> is_late = true.
        await SeedInstrumentedTaskAsync(
            context,
            taskId: 3,
            ownerId: 1,
            workspaceId: workspace.Id,
            assigneeId: 2,
            createdAtUtc: baseCreatedUtc,
            initialDueDate: originalDueDate,
            currentDueDate: originalDueDate,
            offsetMinutes: offsetMinutes,
            status: TaskStatus.Completed,
            activities:
            [
                new TaskActivity
                {
                    TaskRequestId = 3, ActorUserId = 1, ActivityType = TaskActivityType.TaskCreated,
                    CreatedAt = baseCreatedUtc
                },
                new TaskActivity
                {
                    TaskRequestId = 3, ActorUserId = 1, ActivityType = TaskActivityType.UserAssigned,
                    TargetUserId = 2, CreatedAt = baseCreatedUtc
                },
                new TaskActivity
                {
                    TaskRequestId = 3, ActorUserId = 2, ActivityType = TaskActivityType.WorkStarted,
                    FromStatus = TaskStatus.Pending, ToStatus = TaskStatus.InProgress,
                    CreatedAt = baseCreatedUtc.AddHours(2)
                },
                new TaskActivity
                {
                    TaskRequestId = 3, ActorUserId = 2, ActivityType = TaskActivityType.SubmissionCreated,
                    FromStatus = TaskStatus.InProgress, ToStatus = TaskStatus.InReview,
                    CreatedAt = originalDueAtUtc.AddHours(-5)
                },
                new TaskActivity
                {
                    TaskRequestId = 3, ActorUserId = 1, ActivityType = TaskActivityType.ChangesRequested,
                    FromStatus = TaskStatus.InReview, ToStatus = TaskStatus.InProgress,
                    CreatedAt = originalDueAtUtc.AddHours(-2)
                },
                new TaskActivity
                {
                    TaskRequestId = 3, ActorUserId = 2, ActivityType = TaskActivityType.SubmissionCreated,
                    FromStatus = TaskStatus.InProgress, ToStatus = TaskStatus.InReview,
                    CreatedAt = originalDueAtUtc.AddHours(1)
                },
                new TaskActivity
                {
                    TaskRequestId = 3, ActorUserId = 1, ActivityType = TaskActivityType.SubmissionApproved,
                    FromStatus = TaskStatus.InReview, ToStatus = TaskStatus.Completed,
                    CreatedAt = originalDueAtUtc.AddHours(2)
                }
            ]);

        var export = await TaskCollaborationTestServices.MlDatasets(context).ExportCsvAsync(1);
        Assert.True(export.Success);
        Assert.Equal(3, export.Data.TotalCount);
        Assert.Equal(3, export.Data.EligibleCount);

        var rows = export.Data.Rows.ToDictionary(x => x.TaskId);
        Assert.True(rows[1].IsEligible);
        Assert.False(rows[1].IsLate);
        Assert.Equal(string.Empty, rows[1].ExclusionReasonCode);

        Assert.True(rows[2].IsEligible);
        Assert.True(rows[2].IsLate);
        Assert.Equal(string.Empty, rows[2].ExclusionReasonCode);

        Assert.True(rows[3].IsEligible);
        Assert.True(rows[3].IsLate);
        Assert.Equal(TaskInitialAssignmentState.Delegated, rows[3].InitialAssignmentState);
    }

    [Fact]
    public async Task Reopening_a_task_follows_documented_labeling_policy_across_open_recompleted_and_cancelled_states()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var manager = TaskCollaborationTestServices.TaskRequests(context);
        var datasetService = TaskCollaborationTestServices.MlDatasets(context);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

        Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "Reopen policy task",
            Description = "Verify reopen invalidates single-cycle label",
            Category = "Policy",
            Priority = TaskPriority.Medium,
            DueDate = dueDate
        }, 1)).Success);

        // 1. First completion -> eligible and labeled on-time (is_late = false).
        Assert.True((await manager.CompleteTaskAsync(1, new TaskWorkflowCommandDto { Version = 0 }, 1)).Success);
        var initialExport = await datasetService.ExportCsvAsync(1);
        var initialRow = Assert.Single(initialExport.Data.Rows);
        Assert.True(initialRow.IsEligible);
        Assert.False(initialRow.IsLate);
        Assert.Equal(TaskDatasetExclusionReason.None, initialRow.ExclusionReason);

        // 2. Reopen -> no longer completed; excluded as reopened_unfinished with null label.
        Assert.True((await manager.ReopenTaskAsync(1, new TaskWorkflowCommandDto { Version = 1 }, 1)).Success);
        var reopenedOpenExport = await datasetService.ExportCsvAsync(1);
        var reopenedOpenRow = Assert.Single(reopenedOpenExport.Data.Rows);
        Assert.False(reopenedOpenRow.IsEligible);
        Assert.Null(reopenedOpenRow.IsLate);
        Assert.Equal(TaskDatasetExclusionReason.ReopenedUnfinished, reopenedOpenRow.ExclusionReason);
        Assert.Equal("reopened_unfinished", reopenedOpenRow.ExclusionReasonCode);

        // 3. Re-complete after reopen -> excluded as reopened_ambiguous_lifecycle with null label.
        Assert.True((await manager.CompleteTaskAsync(1, new TaskWorkflowCommandDto { Version = 2 }, 1)).Success);
        var recompletedExport = await datasetService.ExportCsvAsync(1);
        var recompletedRow = Assert.Single(recompletedExport.Data.Rows);
        Assert.False(recompletedRow.IsEligible);
        Assert.Null(recompletedRow.IsLate);
        Assert.Equal(TaskDatasetExclusionReason.ReopenedAmbiguousLifecycle, recompletedRow.ExclusionReason);
        Assert.Equal("reopened_ambiguous_lifecycle", recompletedRow.ExclusionReasonCode);
        Assert.EndsWith(",0,reopened_ambiguous_lifecycle,", recompletedExport.Data.CsvContent.TrimEnd());

        // 4. Reopen again and cancel -> excluded as cancelled with null label.
        Assert.True((await manager.ReopenTaskAsync(1, new TaskWorkflowCommandDto { Version = 3 }, 1)).Success);
        Assert.True((await manager.CancelTaskAsync(1, new TaskWorkflowCommandDto { Version = 4 }, 1)).Success);
        var cancelledExport = await datasetService.ExportCsvAsync(1);
        var cancelledRow = Assert.Single(cancelledExport.Data.Rows);
        Assert.False(cancelledRow.IsEligible);
        Assert.Null(cancelledRow.IsLate);
        Assert.Equal(TaskDatasetExclusionReason.Cancelled, cancelledRow.ExclusionReason);
        Assert.Equal("cancelled", cancelledRow.ExclusionReasonCode);
    }

    [Fact]
    public async Task Unfinished_cancelled_deleted_and_missing_initial_deadline_tasks_remain_unlabeled()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var manager = TaskCollaborationTestServices.TaskRequests(context);
        var datasetService = TaskCollaborationTestServices.MlDatasets(context);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));

        // Task 1: Pending unfinished
        Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "Pending task", Description = "Remains pending", Category = "ML",
            Priority = TaskPriority.Low, DueDate = dueDate
        }, 1)).Success);

        // Task 2: InProgress unfinished
        Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "InProgress task", Description = "Started but unfinished", Category = "ML",
            Priority = TaskPriority.Medium, DueDate = dueDate
        }, 1)).Success);
        Assert.True((await manager.StartTaskAsync(2, new TaskWorkflowCommandDto { Version = 0 }, 1)).Success);

        // Task 3: Cancelled
        Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "Cancelled task", Description = "Cancelled before completion", Category = "ML",
            Priority = TaskPriority.High, DueDate = dueDate
        }, 1)).Success);
        Assert.True((await manager.CancelTaskAsync(3, new TaskWorkflowCommandDto { Version = 0 }, 1)).Success);

        // Task 4: Soft-deleted
        Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "Deleted task", Description = "Soft deleted by owner", Category = "ML",
            Priority = TaskPriority.Critical, DueDate = dueDate
        }, 1)).Success);
        Assert.True((await manager.DeleteTask(4, 1)).Success);

        // Task 5: Created without initial deadline, deadline added later via UpdateTask, then completed.
        Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "No initial deadline task", Description = "Deadline added only on later edit", Category = "ML",
            Priority = TaskPriority.Medium, DueDate = null
        }, 1)).Success);
        Assert.True((await manager.UpdateTask(new UpdateTaskRequestDto
        {
            Id = 5, Version = 0, Title = "No initial deadline task",
            Description = "Deadline added only on later edit", Category = "ML",
            Priority = TaskPriority.Medium, DueDate = dueDate
        }, 1)).Success);
        Assert.True((await manager.CompleteTaskAsync(5, new TaskWorkflowCommandDto { Version = 1 }, 1)).Success);

        var export = await datasetService.ExportCsvAsync(1);
        Assert.True(export.Success);
        Assert.Equal(5, export.Data.TotalCount);
        Assert.Equal(0, export.Data.EligibleCount);
        Assert.Equal(5, export.Data.ExcludedCount);
        Assert.All(export.Data.Rows, row =>
        {
            Assert.False(row.IsEligible);
            Assert.Null(row.IsLate);
        });

        var byId = export.Data.Rows.ToDictionary(x => x.TaskId);
        Assert.Equal(TaskDatasetExclusionReason.Unfinished, byId[1].ExclusionReason);
        Assert.Equal(TaskDatasetExclusionReason.Unfinished, byId[2].ExclusionReason);
        Assert.Equal(TaskDatasetExclusionReason.Cancelled, byId[3].ExclusionReason);
        Assert.Equal(TaskDatasetExclusionReason.Deleted, byId[4].ExclusionReason);
        Assert.Equal(TaskDatasetExclusionReason.MissingInitialDeadline, byId[5].ExclusionReason);

        var eligibleOnlyExport = await datasetService.ExportCsvAsync(1, eligibleOnly: true);
        Assert.True(eligibleOnlyExport.Success);
        Assert.Empty(eligibleOnlyExport.Data.Rows);
    }

    [Fact]
    public async Task Concurrent_updates_and_completions_do_not_create_duplicate_snapshots_or_events()
    {
        using var database = new TestDatabase();
        await using (var seed = database.CreateContext())
        {
            var manager = TaskCollaborationTestServices.TaskRequests(seed);
            Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
            {
                Title = "Concurrency task",
                Description = "Verify single snapshot and non-duplicated events",
                Category = "Concurrency",
                Priority = TaskPriority.Medium,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3))
            }, 1)).Success);
        }

        await using var firstContext = database.CreateContext();
        await using var staleUpdateContext = database.CreateContext();
        await staleUpdateContext.TaskRequests.SingleAsync();

        var firstManager = TaskCollaborationTestServices.TaskRequests(firstContext);
        var staleUpdateManager = TaskCollaborationTestServices.TaskRequests(staleUpdateContext);

        var winningUpdate = await firstManager.UpdateTask(new UpdateTaskRequestDto
        {
            Id = 1, Version = 0, Title = "Winning update", Description = "Verify single snapshot and non-duplicated events",
            Category = "Concurrency", Priority = TaskPriority.High,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6))
        }, 1);
        Assert.True(winningUpdate.Success);

        var losingUpdate = await staleUpdateManager.UpdateTask(new UpdateTaskRequestDto
        {
            Id = 1, Version = 0, Title = "Losing update", Description = "Verify single snapshot and non-duplicated events",
            Category = "Concurrency", Priority = TaskPriority.Critical,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(9))
        }, 1);
        Assert.IsAssignableFrom<IConflictResult>(losingUpdate);

        await using var staleCompleteContext = database.CreateContext();
        await staleCompleteContext.TaskRequests.SingleAsync();

        Assert.True((await firstManager.CompleteTaskAsync(1, new TaskWorkflowCommandDto { Version = 1 }, 1)).Success);
        var losingComplete = await TaskCollaborationTestServices.TaskRequests(staleCompleteContext)
            .CompleteTaskAsync(1, new TaskWorkflowCommandDto { Version = 1 }, 1);
        Assert.IsAssignableFrom<IConflictResult>(losingComplete);

        await using var verify = database.CreateContext();
        Assert.Single(await verify.TaskPlanningSnapshots.ToListAsync());
        Assert.Equal(2, await verify.TaskPlanningRevisions.CountAsync());
        Assert.Single(await verify.TaskActivities.Where(x =>
            x.ActivityType == TaskActivityType.TaskDetailsUpdated).ToListAsync());
        Assert.Single(await verify.TaskActivities.Where(x =>
            x.ActivityType == TaskActivityType.TaskCompleted).ToListAsync());
    }

    [Fact]
    public async Task Export_respects_authorization_boundaries_across_personal_shared_and_workspace_tasks()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspace = new Workspace
        {
            Name = "Isolated Workspace",
            Members =
            [
                new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner, IsActive = true },
                new WorkspaceMember { UserId = 2, Role = WorkspaceRole.Member, IsActive = true }
            ]
        };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();

        var manager = TaskCollaborationTestServices.TaskRequests(context);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));

        // Task 1: User 1 private task (not shared)
        Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "User1 private", Description = "Private to user 1", Category = "Private",
            Priority = TaskPriority.Medium, DueDate = dueDate
        }, 1)).Success);

        // Task 2: User 1 task with Visibility = Public, still not shared with User 3 (must not leak in global export!)
        Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "User1 public visibility", Description = "Still must not leak globally", Category = "Public",
            Priority = TaskPriority.High, DueDate = dueDate
        }, 1)).Success);
        var task2 = await context.TaskRequests.SingleAsync(x => x.Id == 2);
        task2.Visibility = TaskVisibility.Public;

        // Task 3: User 1 task shared with User 2 (accepted TaskShare), but only pending invitation for User 3
        Assert.True((await manager.AddTaskRequestAsync(new TaskRequestCreateDto
        {
            Title = "Shared with user2", Description = "Collaborator access test", Category = "Shared",
            Priority = TaskPriority.Low, DueDate = dueDate
        }, 1)).Success);
        context.TaskShares.Add(new TaskShare
        {
            TaskRequestId = 3, SharedWithUserId = 2, Permission = TaskPermission.View, SharedAt = DateTime.UtcNow
        });
        context.TaskShareInvitations.Add(new TaskShareInvitation
        {
            TaskRequestId = 3, InvitedByUserId = 1, InvitedUserId = 3,
            Permission = TaskPermission.Edit, Status = TaskShareInvitationStatus.Pending
        });

        // Task 4: Workspace task in Workspace 1 (User 1 and User 2 are active members; User 3 is not)
        var wsTaskResult = await manager.AddWorkspaceTaskAsync(workspace.Id, new WorkspaceTaskCreateDto
        {
            Title = "Workspace task", Description = "Accessible to workspace members", Category = "Workspace",
            Priority = TaskPriority.Critical, DueDate = dueDate, AssigneeUserId = 2
        }, 1);
        Assert.True(wsTaskResult.Success);
        await context.SaveChangesAsync();

        var datasetService = TaskCollaborationTestServices.MlDatasets(context);

        // User 1 sees tasks 1, 2, 3, 4
        var user1Export = await datasetService.ExportCsvAsync(1);
        Assert.True(user1Export.Success);
        Assert.Equal([1, 2, 3, 4], user1Export.Data.Rows.Select(x => x.TaskId));

        // User 2 sees only shared task 3 and workspace task 4
        var user2Export = await datasetService.ExportCsvAsync(2);
        Assert.True(user2Export.Success);
        Assert.Equal([3, 4], user2Export.Data.Rows.Select(x => x.TaskId));

        // User 3 (unrelated, only pending invitation) sees 0 tasks
        var user3Export = await datasetService.ExportCsvAsync(3);
        Assert.True(user3Export.Success);
        Assert.Empty(user3Export.Data.Rows);

        // User 3 requesting workspace 1 export is denied with 403
        var user3WorkspaceController = new MlDatasetController(datasetService, new FixedCurrentUser(3))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var forbiddenResult = await user3WorkspaceController.ExportCsv(workspaceId: workspace.Id);
        var objectResult = Assert.IsType<ObjectResult>(forbiddenResult);
        Assert.Equal(403, objectResult.StatusCode);

        // Removing User 2 from Task 3 and Workspace 1 immediately revokes User 2's export access
        var shareManager = TaskCollaborationTestServices.TaskShares(context, 1);
        var task3Version = (await context.TaskRequests.SingleAsync(x => x.Id == 3)).Version;
        Assert.True((await shareManager.RemoveParticipantAsync(3, 2, task3Version)).Success);

        var task4Version = (await context.TaskRequests.SingleAsync(x => x.Id == 4)).Version;
        Assert.True((await manager.UnassignTaskAsync(4, new TaskWorkflowCommandDto { Version = task4Version }, 1)).Success);
        var member2 = await context.WorkspaceMembers.SingleAsync(x => x.WorkspaceId == workspace.Id && x.UserId == 2);
        var workspaceManager = TaskCollaborationTestServices.Workspaces(context, 1);
        Assert.True((await workspaceManager.RemoveMemberAsync(workspace.Id, 2,
            new WorkspaceVersionDto { Version = member2.Version })).Success);

        var user2AfterRevocation = await datasetService.ExportCsvAsync(2);
        Assert.True(user2AfterRevocation.Success);
        Assert.Empty(user2AfterRevocation.Data.Rows);
        Assert.False((await datasetService.ExportCsvAsync(2, workspaceId: workspace.Id)).Success);
    }

    [Fact]
    public async Task Historical_tasks_without_reliable_snapshots_or_completion_histories_are_never_fabricated()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var createdAt = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
        var dueDate = new DateOnly(2026, 5, 15);

        // Historical Task 1: Pre-revision legacy completed task with no snapshot and no planning revisions.
        context.TaskRequests.Add(new TaskRequest
        {
            Id = 1, OwnerId = 1, Title = "Legacy task 1", Description = "Created before snapshots",
            Category = "Legacy", Priority = TaskPriority.High, Status = TaskStatus.Completed,
            DueDate = dueDate, Activity = true, CreatedAt = createdAt
        });

        // Historical Task 2: Legacy task whose Revision 1 was written on a later edit (ChangedAt > CreatedAt).
        context.TaskRequests.Add(new TaskRequest
        {
            Id = 2, OwnerId = 1, Title = "Legacy task 2", Description = "Edited after creation",
            Category = "Legacy", Priority = TaskPriority.Medium, Status = TaskStatus.Completed,
            DueDate = dueDate, Activity = true, CreatedAt = createdAt
        });
        context.TaskPlanningRevisions.Add(new TaskPlanningRevision
        {
            TaskRequestId = 2, RevisionNumber = 1, ChangedByUserId = 1,
            ChangedAt = createdAt.AddDays(2), Title = "Legacy task 2",
            Description = "Edited after creation", Category = "Legacy",
            Priority = TaskPriority.Medium, DueDate = dueDate
        });

        // Historical Task 3: Has TaskPlanningSnapshot and Status = Completed, but missing completion activity in TaskActivities.
        var task3 = new TaskRequest
        {
            Id = 3, OwnerId = 1, Title = "Incomplete history task", Description = "Missing completion event",
            Category = "Legacy", Priority = TaskPriority.Low, Status = TaskStatus.Completed,
            DueDate = dueDate, Activity = true, CreatedAt = createdAt
        };
        context.TaskRequests.Add(task3);
        context.TaskPlanningSnapshots.Add(new TaskPlanningSnapshot
        {
            TaskRequestId = 3,
            SchemaVersion = TaskPlanningSnapshot.CurrentSchemaVersion,
            CreatedAtUtc = createdAt,
            InitialUtcOffsetMinutes = 0,
            InitialPriority = TaskPriority.Low,
            InitialDueDate = dueDate,
            OriginalDueAtUtc = TaskPlanningSnapshot.ComputeOriginalDueAtUtc(dueDate, 0),
            InitialPlannedCalendarDays = 5,
            InitialPlannedDurationSeconds = 432000,
            InitialAssignmentState = TaskInitialAssignmentState.Unassigned,
            IsWorkspaceTask = false,
            InitialVisibility = TaskVisibility.Private,
            InitialTitleLength = task3.Title.Length,
            InitialDescriptionLength = task3.Description.Length
        });
        context.TaskActivities.Add(new TaskActivity
        {
            TaskRequestId = 3, ActorUserId = 1, ActivityType = TaskActivityType.TaskCreated,
            CreatedAt = createdAt
        });
        await context.SaveChangesAsync();

        var datasetService = TaskCollaborationTestServices.MlDatasets(context);
        var export = await datasetService.ExportCsvAsync(1);
        Assert.True(export.Success);
        Assert.Equal(3, export.Data.TotalCount);
        Assert.Equal(0, export.Data.EligibleCount);

        var rows = export.Data.Rows.ToDictionary(x => x.TaskId);
        Assert.False(rows[1].IsEligible);
        Assert.Equal(TaskDatasetExclusionReason.MissingInitialSnapshot, rows[1].ExclusionReason);
        Assert.Null(rows[1].IsLate);
        Assert.Null(rows[1].InitialDueDate);
        Assert.Null(rows[1].OriginalDueAtUtc);
        Assert.Null(rows[1].InitialPriority);

        Assert.False(rows[2].IsEligible);
        Assert.Equal(TaskDatasetExclusionReason.MissingInitialSnapshot, rows[2].ExclusionReason);
        Assert.Null(rows[2].IsLate);
        Assert.Null(rows[2].InitialDueDate);

        Assert.False(rows[3].IsEligible);
        Assert.Equal(TaskDatasetExclusionReason.IncompleteCompletionHistory, rows[3].ExclusionReason);
        Assert.Null(rows[3].IsLate);

        var eligibility = await datasetService.AnalyzeEligibilityAsync(1);
        Assert.True(eligibility.Success);
        var analyses = eligibility.Data.ToDictionary(x => x.TaskId);
        Assert.False(analyses[1].HasAuthoritativeInitialSnapshot);
        Assert.False(analyses[1].HasLegacyPlanningRevision);
        Assert.False(analyses[2].HasAuthoritativeInitialSnapshot);
        Assert.True(analyses[2].HasLegacyPlanningRevision);
        Assert.False(analyses[2].LegacyRevisionMatchesCreationTimestamp);
        Assert.True(analyses[3].HasAuthoritativeInitialSnapshot);
        Assert.False(analyses[3].HasCompletionActivity);
    }

    [Fact]
    public async Task Csv_contains_no_sensitive_text_or_personal_identifiers_and_passes_data_leakage_checks()
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        var workspace = new Workspace
        {
            Name = "SECRET_WORKSPACE_NAME_991",
            Members =
            [
                new WorkspaceMember { UserId = 1, Role = WorkspaceRole.Owner, IsActive = true },
                new WorkspaceMember { UserId = 2, Role = WorkspaceRole.Member, IsActive = true }
            ]
        };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();

        var manager = TaskCollaborationTestServices.TaskRequests(context);
        var collaboration = TaskCollaborationTestServices.Create(context);
        var datasetService = TaskCollaborationTestServices.MlDatasets(context);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

        var created = await manager.AddWorkspaceTaskAsync(workspace.Id, new WorkspaceTaskCreateDto
        {
            Title = "CONFIDENTIAL_TASK_TITLE_ALPHA",
            Description = "SENSITIVE_DESCRIPTION_PAYLOAD_BETA",
            Category = "PRIVATE_CATEGORY_GAMMA",
            Priority = TaskPriority.Low,
            DueDate = dueDate,
            UtcOffsetMinutes = 60,
            AssigneeUserId = null
        }, 1);
        Assert.True(created.Success);
        var taskId = created.Data.Id!.Value;

        // Capture creation-time feature vector from CSV before any edits, assignments, submissions, reviews, or completion.
        var exportAtCreation = await datasetService.ExportCsvAsync(1);
        Assert.True(exportAtCreation.Success);
        var creationCsvLines = ParseCsvLines(exportAtCreation.Data.CsvContent);
        Assert.Equal(2, creationCsvLines.Count);
        var header = creationCsvLines[0];
        var creationValues = creationCsvLines[1];

        // Perform post-creation mutations: edit deadline/priority/title/description, assign, start, submit, review changes requested, resubmit, approve.
        Assert.True((await manager.UpdateTask(new UpdateTaskRequestDto
        {
            Id = taskId, Version = 0,
            Title = "UPDATED_SECRET_TITLE_DELTA",
            Description = "UPDATED_SENSITIVE_DESCRIPTION_EPSILON_WITH_EXTRA_TEXT",
            Category = "UPDATED_PRIVATE_CATEGORY_ZETA",
            Priority = TaskPriority.Critical,
            DueDate = dueDate.AddDays(7),
            UtcOffsetMinutes = 60
        }, 1)).Success);

        Assert.True((await manager.AssignTaskAsync(taskId, new AssignTaskDto { AssigneeUserId = 2, Version = 1 }, 1)).Success);
        Assert.True((await manager.StartTaskAsync(taskId, new TaskWorkflowCommandDto { Version = 2 }, 2)).Success);
        Assert.True((await collaboration.SendMessageAsync(taskId, 2, "PRIVATE_DISCUSSION_MESSAGE_ETA")).Success);

        context.TaskAttachments.Add(new TaskAttachment
        {
            TaskRequestId = taskId,
            UploaderUserId = 2,
            Kind = TaskAttachmentKind.Task,
            OriginalFileName = "SECRET_ATTACHMENT_FILENAME_THETA.pdf",
            StorageKey = "4d6e3cd1ff7e4960bfaa9d4b8d280cb9",
            ContentType = "application/pdf",
            SizeBytes = 1024
        });
        await context.SaveChangesAsync();

        var sub1 = await manager.SubmitAsync(taskId, new CreateTaskSubmissionDto
        {
            Version = 3,
            Content = "SUBMISSION_SECRET_CONTENT_IOTA"
        }, 2);
        Assert.True(sub1.Success);

        var rev1 = await manager.ReviewAsync(taskId, sub1.Data.Id, new ReviewTaskSubmissionDto
        {
            Version = 4,
            Decision = TaskReviewDecision.ChangesRequested,
            Feedback = "REVIEW_FEEDBACK_SECRET_KAPPA"
        }, 1);
        Assert.True(rev1.Success);

        var sub2 = await manager.SubmitAsync(taskId, new CreateTaskSubmissionDto
        {
            Version = 5,
            Content = "SECOND_SUBMISSION_SECRET_LAMBDA"
        }, 2);
        Assert.True(sub2.Success);

        var rev2 = await manager.ReviewAsync(taskId, sub2.Data.Id, new ReviewTaskSubmissionDto
        {
            Version = 6,
            Decision = TaskReviewDecision.Approved,
            Feedback = "APPROVED_FEEDBACK_SECRET_MU"
        }, 1);
        Assert.True(rev2.Success);

        var controller = new MlDatasetController(datasetService, new FixedCurrentUser(1))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var actionResult = await controller.ExportCsv();
        var fileResult = Assert.IsType<FileContentResult>(actionResult);
        Assert.Equal("text/csv; charset=utf-8", fileResult.ContentType);
        Assert.Equal(TaskPlanningSnapshot.CurrentSchemaVersion,
            controller.Response.Headers[MlDatasetController.SchemaVersionHeader].ToString());

        var csvText = Encoding.UTF8.GetString(fileResult.FileContents);

        // 1. Privacy & PII check: none of the sensitive text, filenames, messages, usernames, or emails appear in CSV.
        string[] forbiddenTokens =
        [
            "CONFIDENTIAL_TASK_TITLE_ALPHA",
            "SENSITIVE_DESCRIPTION_PAYLOAD_BETA",
            "PRIVATE_CATEGORY_GAMMA",
            "UPDATED_SECRET_TITLE_DELTA",
            "UPDATED_SENSITIVE_DESCRIPTION_EPSILON_WITH_EXTRA_TEXT",
            "UPDATED_PRIVATE_CATEGORY_ZETA",
            "PRIVATE_DISCUSSION_MESSAGE_ETA",
            "SECRET_ATTACHMENT_FILENAME_THETA",
            "SUBMISSION_SECRET_CONTENT_IOTA",
            "REVIEW_FEEDBACK_SECRET_KAPPA",
            "SECOND_SUBMISSION_SECRET_LAMBDA",
            "APPROVED_FEEDBACK_SECRET_MU",
            "SECRET_WORKSPACE_NAME_991",
            "user1",
            "user2",
            "user3",
            "example.test"
        ];
        foreach (var token in forbiddenTokens)
            Assert.DoesNotContain(token, csvText, StringComparison.OrdinalIgnoreCase);

        // 2. Schema check: no raw user ID columns or post-creation leakage columns exist in schema.
        var schema = datasetService.GetSchema();
        Assert.DoesNotContain(schema.Columns, c =>
            c.Name.Contains("user", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("owner", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("assignee_id", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("completed_at", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("review", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("submission", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("current_", StringComparison.OrdinalIgnoreCase));

        // 3. Data leakage invariance check: every metadata & feature column (all columns except is_eligible, exclusion_reason, is_late)
        // must be 100% identical between creation time and post-completion time.
        var completionCsvLines = ParseCsvLines(csvText);
        Assert.Equal(2, completionCsvLines.Count);
        var completionValues = completionCsvLines[1];

        // Columns 0..19 are dataset_schema_version, task_id, and the 18 prediction-time features.
        // Columns 20..22 are is_eligible, exclusion_reason, is_late.
        Assert.Equal(23, header.Length);
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(creationValues[i], completionValues[i]);
        }

        Assert.Equal("0", creationValues[20]);
        Assert.Equal("unfinished", creationValues[21]);
        Assert.Equal(string.Empty, creationValues[22]);

        Assert.Equal("1", completionValues[20]);
        Assert.Equal(string.Empty, completionValues[21]);
        Assert.Equal("0", completionValues[22]);
    }

    [Fact]
    public void Migration_creates_snapshot_table_with_unique_task_index_without_fabricating_legacy_data()
    {
        var migration = new AddTaskPlanningSnapshots
        {
            ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL"
        };

        Assert.DoesNotContain(migration.UpOperations,
            op => op is SqlOperation or InsertDataOperation or UpdateDataOperation);
        var createTable = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("TaskPlanningSnapshots", createTable.Name);
        Assert.DoesNotContain(createTable.Columns, c =>
            c.Name is "Title" or "Description" or "Category" or "OwnerId" or "AssigneeUserId" or "ChangedByUserId");
        var uniqueIndex = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>(),
            ix => ix.Name == "IX_TaskPlanningSnapshots_TaskRequestId");
        Assert.True(uniqueIndex.IsUnique);
    }

    private static List<string[]> ParseCsvLines(string csv) =>
        csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(','))
            .ToList();

    private static async Task SeedInstrumentedTaskAsync(
        TaskTrackerDbContext context,
        int taskId,
        int ownerId,
        int? workspaceId,
        int? assigneeId,
        DateTime createdAtUtc,
        DateOnly? initialDueDate,
        DateOnly? currentDueDate,
        int offsetMinutes,
        TaskStatus status,
        IReadOnlyList<TaskActivity> activities)
    {
        var task = new TaskRequest
        {
            Id = taskId,
            OwnerId = ownerId,
            WorkspaceId = workspaceId,
            AssigneeUserId = assigneeId,
            Title = $"Seeded task {taskId}",
            Description = $"Seeded description {taskId}",
            Category = "Tests",
            Priority = TaskPriority.Medium,
            Status = status,
            DueDate = currentDueDate,
            Activity = true,
            Visibility = TaskVisibility.Private,
            CreatedAt = createdAtUtc
        };
        context.TaskRequests.Add(task);

        var originalDueAtUtc = initialDueDate.HasValue
            ? TaskPlanningSnapshot.ComputeOriginalDueAtUtc(initialDueDate.Value, offsetMinutes)
            : (DateTime?)null;
        var assignmentState = !assigneeId.HasValue
            ? TaskInitialAssignmentState.Unassigned
            : assigneeId.Value == ownerId
                ? TaskInitialAssignmentState.SelfAssigned
                : TaskInitialAssignmentState.Delegated;

        context.TaskPlanningSnapshots.Add(new TaskPlanningSnapshot
        {
            TaskRequestId = taskId,
            SchemaVersion = TaskPlanningSnapshot.CurrentSchemaVersion,
            CreatedAtUtc = createdAtUtc,
            InitialUtcOffsetMinutes = offsetMinutes,
            InitialPriority = TaskPriority.Medium,
            InitialDueDate = initialDueDate,
            OriginalDueAtUtc = originalDueAtUtc,
            InitialPlannedCalendarDays = initialDueDate.HasValue ? 5 : null,
            InitialPlannedDurationSeconds = originalDueAtUtc.HasValue
                ? (long)(originalDueAtUtc.Value - createdAtUtc).TotalSeconds
                : null,
            InitialAssignmentState = assignmentState,
            IsWorkspaceTask = workspaceId.HasValue,
            InitialVisibility = TaskVisibility.Private,
            InitialTitleLength = task.Title.Length,
            InitialDescriptionLength = task.Description.Length
        });

        context.TaskActivities.AddRange(activities);
        await context.SaveChangesAsync();
    }
}
