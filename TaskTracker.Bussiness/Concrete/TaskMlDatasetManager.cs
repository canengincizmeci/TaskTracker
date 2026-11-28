using System.Globalization;
using System.Text;
using TaskTracker.Bussiness.Abstract;
using TaskTracker.Bussiness.Constanst;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Core.Utilities.Results;
using TaskTracker.DataAccess.Abstract;
using TaskTracker.Entities.DTOs;
using TaskStatus = TaskTracker.Core.Utilities.Enums.TaskStatus;

namespace TaskTracker.Bussiness.Concrete;

public class TaskMlDatasetManager(
    ITaskMlDatasetDal datasetDal,
    IWorkspaceDal workspaceDal) : ITaskMlDatasetService
{
    public const string SchemaVersion = TaskPlanningSnapshot.CurrentSchemaVersion;
    public const string MissingValue = "";
    public const string CsvFileName = "task-late-completion-dataset-v1.0.csv";
    public const string DatasetLimitExceeded = "The authorized dataset exceeds the request budget. Select a smaller workspace scope.";

    private static readonly IReadOnlyList<TaskMlDatasetColumnDefinitionDto> ColumnDefinitions =
    [
        new()
        {
            Name = "dataset_schema_version",
            DataType = "string",
            Role = "metadata",
            AllowMissing = false,
            Definition = "Dataset schema version identifier (1.0)."
        },
        new()
        {
            Name = "task_id",
            DataType = "integer",
            Role = "metadata",
            AllowMissing = false,
            Definition = "Row number local to this export. Never use this metadata as a predictive feature or join key."
        },
        new()
        {
            Name = "created_at_utc",
            DataType = "datetime_utc_iso8601",
            Role = "feature",
            AllowMissing = true,
            Definition = "Immutable UTC timestamp when the task and its initial planning snapshot were created."
        },
        new()
        {
            Name = "initial_utc_offset_minutes",
            DataType = "integer",
            Role = "feature",
            AllowMissing = true,
            Definition = "Creator timezone offset from UTC in minutes [-720, 840] captured at task creation."
        },
        new()
        {
            Name = "created_day_of_week",
            DataType = "integer",
            Role = "feature",
            AllowMissing = true,
            Definition = "Day of week [0=Sunday..6=Saturday] in the creator's local offset at task creation."
        },
        new()
        {
            Name = "created_hour_local",
            DataType = "integer",
            Role = "feature",
            AllowMissing = true,
            Definition = "Hour of day [0..23] in the creator's local offset at task creation."
        },
        new()
        {
            Name = "has_initial_due_date",
            DataType = "binary_int",
            Role = "feature",
            AllowMissing = true,
            Definition = "1 if an initial due date was specified at task creation; 0 if created without a deadline."
        },
        new()
        {
            Name = "initial_due_date",
            DataType = "date_iso8601",
            Role = "feature",
            AllowMissing = true,
            Definition = "Immutable calendar due date (yyyy-MM-dd) specified at task creation."
        },
        new()
        {
            Name = "original_due_at_utc",
            DataType = "datetime_utc_iso8601",
            Role = "feature",
            AllowMissing = true,
            Definition = "Immutable UTC deadline timestamp representing end-of-day (23:59:59.999999) of initial_due_date in initial_utc_offset_minutes."
        },
        new()
        {
            Name = "initial_planned_calendar_days",
            DataType = "integer",
            Role = "feature",
            AllowMissing = true,
            Definition = "Calendar days between the creator's local creation date and initial_due_date."
        },
        new()
        {
            Name = "initial_planned_duration_hours",
            DataType = "float",
            Role = "feature",
            AllowMissing = true,
            Definition = "Initially planned duration in hours from created_at_utc to original_due_at_utc."
        },
        new()
        {
            Name = "initial_priority",
            DataType = "categorical_string",
            Role = "feature",
            AllowMissing = true,
            Definition = "Initial task priority category at creation (Low, Medium, High, Critical)."
        },
        new()
        {
            Name = "initial_priority_level",
            DataType = "ordinal_int",
            Role = "feature",
            AllowMissing = true,
            Definition = "Ordinal encoding of initial_priority (0=Low, 1=Medium, 2=High, 3=Critical)."
        },
        new()
        {
            Name = "initial_assignment_state",
            DataType = "categorical_string",
            Role = "feature",
            AllowMissing = true,
            Definition = "Initial responsibility state at creation (Unassigned, SelfAssigned, Delegated) without exposing user IDs."
        },
        new()
        {
            Name = "is_initially_assigned",
            DataType = "binary_int",
            Role = "feature",
            AllowMissing = true,
            Definition = "1 if an explicit assignee was set at task creation (SelfAssigned or Delegated); 0 if Unassigned."
        },
        new()
        {
            Name = "is_initially_delegated",
            DataType = "binary_int",
            Role = "feature",
            AllowMissing = true,
            Definition = "1 if initially assigned to a workspace member other than the task owner; 0 otherwise."
        },
        new()
        {
            Name = "is_workspace_task",
            DataType = "binary_int",
            Role = "feature",
            AllowMissing = true,
            Definition = "1 if the task was created inside a workspace; 0 if created as a personal task."
        },
        new()
        {
            Name = "initial_visibility",
            DataType = "categorical_string",
            Role = "feature",
            AllowMissing = true,
            Definition = "Initial visibility setting at creation (Private, Shared, Public)."
        },
        new()
        {
            Name = "initial_title_length",
            DataType = "integer",
            Role = "feature",
            AllowMissing = true,
            Definition = "Character count of the initial task title at creation (no text content is exported)."
        },
        new()
        {
            Name = "initial_description_length",
            DataType = "integer",
            Role = "feature",
            AllowMissing = true,
            Definition = "Character count of the initial task description at creation (no text content is exported)."
        },
        new()
        {
            Name = "is_eligible",
            DataType = "binary_int",
            Role = "eligibility",
            AllowMissing = false,
            Definition = "1 if the record has an authoritative initial snapshot, an initial deadline, and an unambiguous single-cycle completion timestamp; 0 otherwise."
        },
        new()
        {
            Name = "exclusion_reason",
            DataType = "categorical_string",
            Role = "eligibility",
            AllowMissing = true,
            Definition = "Empty when is_eligible is 1; otherwise the explicit reason code explaining why the record is excluded from supervised training labels."
        },
        new()
        {
            Name = "is_late",
            DataType = "binary_int",
            Role = "label",
            AllowMissing = true,
            Definition = "Supervised binary outcome label (1 if completed_at > original_due_at_utc, 0 if completed_at <= original_due_at_utc). Empty for all ineligible records."
        }
    ];

    private static readonly IReadOnlyDictionary<string, string> ExclusionReasonDefinitions =
        new Dictionary<string, string>
        {
            [ToReasonCode(TaskDatasetExclusionReason.MissingInitialSnapshot)] =
                "Historical or uninstrumented task lacks an immutable initial planning snapshot; original deadline and creation features are not fabricated.",
            [ToReasonCode(TaskDatasetExclusionReason.Deleted)] =
                "Task was soft-deleted and is excluded from completion outcome modeling.",
            [ToReasonCode(TaskDatasetExclusionReason.MissingInitialDeadline)] =
                "Task was created without an initial due date; later deadline edits are never substituted for original_due_at.",
            [ToReasonCode(TaskDatasetExclusionReason.ReopenedUnfinished)] =
                "Task was reopened after a terminal state and is currently unfinished; prior completion is invalidated.",
            [ToReasonCode(TaskDatasetExclusionReason.ReopenedAmbiguousLifecycle)] =
                "Task was reopened and later re-completed; multi-cycle completion timing is ambiguous relative to the original deadline.",
            [ToReasonCode(TaskDatasetExclusionReason.Cancelled)] =
                "Task was cancelled and did not reach a valid completion outcome.",
            [ToReasonCode(TaskDatasetExclusionReason.Unfinished)] =
                "Task is still open (Pending, InProgress, or InReview) and has not been completed.",
            [ToReasonCode(TaskDatasetExclusionReason.IncompleteCompletionHistory)] =
                "Task status is Completed but activity history is missing a valid single completion event or has inconsistent timestamps."
        };

    public TaskMlDatasetSchemaDto GetSchema() => new()
    {
        SchemaVersion = SchemaVersion,
        MissingValueRepresentation = MissingValue,
        DeadlineTimezoneInterpretation =
            "InitialDueDate (DateOnly) is interpreted as end of local calendar day (23:59:59.999999) in the creator's captured InitialUtcOffsetMinutes and converted to UTC (OriginalDueAtUtc).",
        LabelFormula = "is_late = completed_at > original_due_at",
        Columns = ColumnDefinitions,
        ExclusionReasons = ExclusionReasonDefinitions
    };

    public async Task<IDataResult<TaskMlDatasetExportDto>> ExportCsvAsync(int currentUserId, int? workspaceId = null,
        bool eligibleOnly = false, CancellationToken ct = default)
    {
        if (currentUserId <= 0)
            return new ErrorDataResult<TaskMlDatasetExportDto>(Messages.AuthorizationDenied);

        if (workspaceId.HasValue &&
            await workspaceDal.GetActiveMembershipAsync(workspaceId.Value, currentUserId) is null)
            return new ErrorDataResult<TaskMlDatasetExportDto>(Messages.AuthorizationDenied);

        var records = await datasetDal.GetAuthorizedRecordsAsync(currentUserId, workspaceId, ct);
        if (records is null)
            return new ErrorDataResult<TaskMlDatasetExportDto>(DatasetLimitExceeded);
        var allRows = records.Select(EvaluateRecord).ToList();
        var exportedRows = eligibleOnly
            ? allRows.Where(x => x.IsEligible).ToList()
            : allRows;

        var csv = BuildCsv(exportedRows);
        return new SuccessDataResult<TaskMlDatasetExportDto>(new TaskMlDatasetExportDto
        {
            SchemaVersion = SchemaVersion,
            MissingValueRepresentation = MissingValue,
            FileName = CsvFileName,
            CsvContent = csv,
            TotalCount = exportedRows.Count,
            EligibleCount = exportedRows.Count(x => x.IsEligible),
            ExcludedCount = exportedRows.Count(x => !x.IsEligible),
            Columns = ColumnDefinitions,
            Rows = exportedRows
        });
    }

    public async Task<IDataResult<List<TaskHistoricalEligibilityDto>>> AnalyzeEligibilityAsync(int currentUserId,
        int? workspaceId = null, CancellationToken ct = default)
    {
        if (currentUserId <= 0)
            return new ErrorDataResult<List<TaskHistoricalEligibilityDto>>(Messages.AuthorizationDenied);

        if (workspaceId.HasValue &&
            await workspaceDal.GetActiveMembershipAsync(workspaceId.Value, currentUserId) is null)
            return new ErrorDataResult<List<TaskHistoricalEligibilityDto>>(Messages.AuthorizationDenied);

        var records = await datasetDal.GetAuthorizedRecordsAsync(currentUserId, workspaceId, ct);
        if (records is null)
            return new ErrorDataResult<List<TaskHistoricalEligibilityDto>>(DatasetLimitExceeded);
        var analyses = records.Select(AnalyzeRecordEligibility).ToList();
        return new SuccessDataResult<List<TaskHistoricalEligibilityDto>>(analyses);
    }

    public static TaskMlDatasetRowDto EvaluateRecord(TaskMlDatasetSourceRecord record)
    {
        var snapshot = record.Snapshot;
        var (isEligible, reason, isLate) = EvaluateOutcome(record);

        if (snapshot is null)
        {
            return new TaskMlDatasetRowDto
            {
                DatasetSchemaVersion = SchemaVersion,
                TaskId = record.TaskId,
                IsEligible = false,
                ExclusionReason = reason,
                ExclusionReasonCode = ToReasonCode(reason),
                IsLate = null
            };
        }

        var createdAtUtc = AsUtc(snapshot.CreatedAtUtc);
        var originalDueAtUtc = snapshot.OriginalDueAtUtc.HasValue
            ? AsUtc(snapshot.OriginalDueAtUtc.Value)
            : (DateTime?)null;
        var localCreatedAt = createdAtUtc.AddMinutes(snapshot.InitialUtcOffsetMinutes);
        var plannedDurationHours = originalDueAtUtc.HasValue
            ? Math.Round((originalDueAtUtc.Value - createdAtUtc).TotalHours, 4, MidpointRounding.AwayFromZero)
            : (double?)null;

        return new TaskMlDatasetRowDto
        {
            DatasetSchemaVersion = snapshot.SchemaVersion,
            TaskId = record.TaskId,
            CreatedAtUtc = createdAtUtc,
            InitialUtcOffsetMinutes = snapshot.InitialUtcOffsetMinutes,
            CreatedDayOfWeek = (int)localCreatedAt.DayOfWeek,
            CreatedHourLocal = localCreatedAt.Hour,
            HasInitialDueDate = snapshot.InitialDueDate.HasValue,
            InitialDueDate = snapshot.InitialDueDate,
            OriginalDueAtUtc = originalDueAtUtc,
            InitialPlannedCalendarDays = snapshot.InitialPlannedCalendarDays,
            InitialPlannedDurationHours = plannedDurationHours,
            InitialPriority = snapshot.InitialPriority,
            InitialPriorityLevel = PriorityLevel(snapshot.InitialPriority),
            InitialAssignmentState = snapshot.InitialAssignmentState,
            IsInitiallyAssigned = snapshot.InitialAssignmentState != TaskInitialAssignmentState.Unassigned,
            IsInitiallyDelegated = snapshot.InitialAssignmentState == TaskInitialAssignmentState.Delegated,
            IsWorkspaceTask = snapshot.IsWorkspaceTask,
            InitialVisibility = snapshot.InitialVisibility,
            InitialTitleLength = snapshot.InitialTitleLength,
            InitialDescriptionLength = snapshot.InitialDescriptionLength,
            IsEligible = isEligible,
            ExclusionReason = reason,
            ExclusionReasonCode = ToReasonCode(reason),
            IsLate = isEligible ? isLate : null
        };
    }

    private static TaskHistoricalEligibilityDto AnalyzeRecordEligibility(TaskMlDatasetSourceRecord record)
    {
        var (isEligible, reason, _) = EvaluateOutcome(record);
        var firstRevision = record.RevisionProvenance.FirstOrDefault(x => x.RevisionNumber == 1);
        var matchesCreation = firstRevision is not null &&
            AsUtc(firstRevision.ChangedAt) == AsUtc(record.CreatedAt);
        var hasCompletion = record.Activities.Any(a =>
            a.ToStatus == TaskStatus.Completed &&
            a.ActivityType is TaskActivityType.TaskCompleted or TaskActivityType.SubmissionApproved);
        var wasReopened = record.Activities.Any(a => a.ActivityType == TaskActivityType.TaskReopened);

        var analysis = reason switch
        {
            TaskDatasetExclusionReason.None =>
                "Eligible: authoritative initial planning snapshot, initial deadline, and unambiguous single-cycle completion timestamp are present.",
            TaskDatasetExclusionReason.MissingInitialSnapshot =>
                firstRevision is null
                    ? "Excluded: pre-snapshot historical task has no TaskPlanningSnapshot and no planning revision history. Original deadline and features cannot be reconstructed."
                    : matchesCreation
                        ? "Excluded: historical task has a creation-time TaskPlanningRevision but no TaskPlanningSnapshot (missing initial UTC offset and initial assignment provenance)."
                        : "Excluded: historical task's first TaskPlanningRevision was written on a later edit rather than at task creation; original deadline is unknown.",
            _ => ExclusionReasonDefinitions.GetValueOrDefault(ToReasonCode(reason), "Excluded by dataset labeling policy.")
        };

        return new TaskHistoricalEligibilityDto
        {
            TaskId = record.TaskId,
            HasAuthoritativeInitialSnapshot = record.Snapshot is not null,
            HasLegacyPlanningRevision = firstRevision is not null,
            LegacyRevisionMatchesCreationTimestamp = matchesCreation,
            HasInitialUtcOffsetProvenance = record.Snapshot is not null,
            HasCompletionActivity = hasCompletion,
            WasReopened = wasReopened,
            IsEligible = isEligible,
            ExclusionReason = reason,
            ExclusionReasonCode = ToReasonCode(reason),
            Analysis = analysis
        };
    }

    private static (bool IsEligible, TaskDatasetExclusionReason Reason, bool? IsLate) EvaluateOutcome(
        TaskMlDatasetSourceRecord record)
    {
        var snapshot = record.Snapshot;
        if (snapshot is null)
            return (false, TaskDatasetExclusionReason.MissingInitialSnapshot, null);

        var activities = record.Activities;
        var isDeleted = !record.Activity ||
                        record.DeletedAt.HasValue ||
                        activities.Any(a => a.ActivityType == TaskActivityType.TaskDeleted);
        if (isDeleted)
            return (false, TaskDatasetExclusionReason.Deleted, null);

        if (!snapshot.InitialDueDate.HasValue || !snapshot.OriginalDueAtUtc.HasValue)
            return (false, TaskDatasetExclusionReason.MissingInitialDeadline, null);

        var hasReopened = activities.Any(a => a.ActivityType == TaskActivityType.TaskReopened);
        if (hasReopened)
        {
            if (record.Status == TaskStatus.Cancelled)
                return (false, TaskDatasetExclusionReason.Cancelled, null);

            return record.Status == TaskStatus.Completed
                ? (false, TaskDatasetExclusionReason.ReopenedAmbiguousLifecycle, null)
                : (false, TaskDatasetExclusionReason.ReopenedUnfinished, null);
        }

        if (record.Status == TaskStatus.Cancelled ||
            activities.Any(a => a.ActivityType == TaskActivityType.TaskCancelled))
            return (false, TaskDatasetExclusionReason.Cancelled, null);

        if (record.Status != TaskStatus.Completed)
            return (false, TaskDatasetExclusionReason.Unfinished, null);

        var hasCreatedActivity = activities.Any(a => a.ActivityType == TaskActivityType.TaskCreated);
        var completions = activities
            .Where(a => a.ToStatus == TaskStatus.Completed &&
                a.ActivityType is TaskActivityType.TaskCompleted or TaskActivityType.SubmissionApproved)
            .ToList();
        var lastStatusTransition = activities.LastOrDefault(a => a.ToStatus.HasValue);

        if (!hasCreatedActivity ||
            completions.Count != 1 ||
            lastStatusTransition is null ||
            lastStatusTransition.Id != completions[0].Id)
            return (false, TaskDatasetExclusionReason.IncompleteCompletionHistory, null);

        var completedAtUtc = AsUtc(completions[0].CreatedAt);
        var createdAtUtc = AsUtc(snapshot.CreatedAtUtc);
        var originalDueAtUtc = AsUtc(snapshot.OriginalDueAtUtc.Value);

        if (completedAtUtc < createdAtUtc)
            return (false, TaskDatasetExclusionReason.IncompleteCompletionHistory, null);

        var isLate = completedAtUtc > originalDueAtUtc;
        return (true, TaskDatasetExclusionReason.None, isLate);
    }

    public static string BuildCsv(IEnumerable<TaskMlDatasetRowDto> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", ColumnDefinitions.Select(c => c.Name)));

        var rowNumber = 0;
        foreach (var row in rows)
        {
            var fields = new[]
            {
                EscapeCsv(row.DatasetSchemaVersion),
                (++rowNumber).ToString(CultureInfo.InvariantCulture),
                FormatUtc(row.CreatedAtUtc),
                FormatInt(row.InitialUtcOffsetMinutes),
                FormatInt(row.CreatedDayOfWeek),
                FormatInt(row.CreatedHourLocal),
                FormatBool(row.HasInitialDueDate),
                FormatDate(row.InitialDueDate),
                FormatUtc(row.OriginalDueAtUtc),
                FormatInt(row.InitialPlannedCalendarDays),
                FormatDouble(row.InitialPlannedDurationHours),
                row.InitialPriority?.ToString() ?? MissingValue,
                FormatInt(row.InitialPriorityLevel),
                row.InitialAssignmentState?.ToString() ?? MissingValue,
                FormatBool(row.IsInitiallyAssigned),
                FormatBool(row.IsInitiallyDelegated),
                FormatBool(row.IsWorkspaceTask),
                row.InitialVisibility?.ToString() ?? MissingValue,
                FormatInt(row.InitialTitleLength),
                FormatInt(row.InitialDescriptionLength),
                row.IsEligible ? "1" : "0",
                EscapeCsv(row.ExclusionReasonCode),
                row.IsEligible && row.IsLate.HasValue ? (row.IsLate.Value ? "1" : "0") : MissingValue
            };
            sb.AppendLine(string.Join(",", fields));
        }

        return sb.ToString();
    }

    public static string ToReasonCode(TaskDatasetExclusionReason reason) => reason switch
    {
        TaskDatasetExclusionReason.None => string.Empty,
        TaskDatasetExclusionReason.MissingInitialSnapshot => "missing_initial_snapshot",
        TaskDatasetExclusionReason.Deleted => "deleted",
        TaskDatasetExclusionReason.MissingInitialDeadline => "missing_initial_deadline",
        TaskDatasetExclusionReason.ReopenedUnfinished => "reopened_unfinished",
        TaskDatasetExclusionReason.ReopenedAmbiguousLifecycle => "reopened_ambiguous_lifecycle",
        TaskDatasetExclusionReason.Cancelled => "cancelled",
        TaskDatasetExclusionReason.Unfinished => "unfinished",
        TaskDatasetExclusionReason.IncompleteCompletionHistory => "incomplete_completion_history",
        _ => "unknown"
    };

    private static int PriorityLevel(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => 0,
        TaskPriority.Medium => 1,
        TaskPriority.High => 2,
        TaskPriority.Critical => 3,
        _ => 1
    };

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string FormatUtc(DateTime? value) =>
        value.HasValue
            ? AsUtc(value.Value).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture)
            : MissingValue;

    private static string FormatDate(DateOnly? value) =>
        value.HasValue
            ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : MissingValue;

    private static string FormatInt(int? value) =>
        value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : MissingValue;

    private static string FormatDouble(double? value) =>
        value.HasValue
            ? value.Value.ToString("0.####", CultureInfo.InvariantCulture)
            : MissingValue;

    private static string FormatBool(bool? value) =>
        value.HasValue
            ? (value.Value ? "1" : "0")
            : MissingValue;

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return MissingValue;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

        return value;
    }
}
