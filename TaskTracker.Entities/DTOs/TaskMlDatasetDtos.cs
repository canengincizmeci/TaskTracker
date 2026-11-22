using TaskTracker.Core.Entities;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Entities.DTOs;

public sealed class TaskMlDatasetColumnDefinitionDto : IDto
{
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public required string Role { get; init; }
    public required bool AllowMissing { get; init; }
    public required string Definition { get; init; }
}

public sealed class TaskMlDatasetSchemaDto : IDto
{
    public string SchemaVersion { get; init; } = TaskPlanningSnapshot.CurrentSchemaVersion;
    public string MissingValueRepresentation { get; init; } = string.Empty;
    public required string DeadlineTimezoneInterpretation { get; init; }
    public required string LabelFormula { get; init; }
    public required IReadOnlyList<TaskMlDatasetColumnDefinitionDto> Columns { get; init; }
    public required IReadOnlyDictionary<string, string> ExclusionReasons { get; init; }
}

public sealed class TaskMlDatasetRowDto : IDto
{
    public string DatasetSchemaVersion { get; init; } = TaskPlanningSnapshot.CurrentSchemaVersion;
    public int TaskId { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
    public int? InitialUtcOffsetMinutes { get; init; }
    public int? CreatedDayOfWeek { get; init; }
    public int? CreatedHourLocal { get; init; }
    public bool? HasInitialDueDate { get; init; }
    public DateOnly? InitialDueDate { get; init; }
    public DateTime? OriginalDueAtUtc { get; init; }
    public int? InitialPlannedCalendarDays { get; init; }
    public double? InitialPlannedDurationHours { get; init; }
    public TaskPriority? InitialPriority { get; init; }
    public int? InitialPriorityLevel { get; init; }
    public TaskInitialAssignmentState? InitialAssignmentState { get; init; }
    public bool? IsInitiallyAssigned { get; init; }
    public bool? IsInitiallyDelegated { get; init; }
    public bool? IsWorkspaceTask { get; init; }
    public TaskVisibility? InitialVisibility { get; init; }
    public int? InitialTitleLength { get; init; }
    public int? InitialDescriptionLength { get; init; }
    public bool IsEligible { get; init; }
    public TaskDatasetExclusionReason ExclusionReason { get; init; }
    public string ExclusionReasonCode { get; init; } = string.Empty;
    public bool? IsLate { get; init; }
}

public sealed class TaskMlDatasetExportDto : IDto
{
    public string SchemaVersion { get; init; } = TaskPlanningSnapshot.CurrentSchemaVersion;
    public string MissingValueRepresentation { get; init; } = string.Empty;
    public string FileName { get; init; } = "task-late-completion-dataset-v1.0.csv";
    public required string CsvContent { get; init; }
    public int TotalCount { get; init; }
    public int EligibleCount { get; init; }
    public int ExcludedCount { get; init; }
    public required IReadOnlyList<TaskMlDatasetColumnDefinitionDto> Columns { get; init; }
    public required IReadOnlyList<TaskMlDatasetRowDto> Rows { get; init; }
}

public sealed class TaskHistoricalEligibilityDto : IDto
{
    public int TaskId { get; init; }
    public bool HasAuthoritativeInitialSnapshot { get; init; }
    public bool HasLegacyPlanningRevision { get; init; }
    public bool LegacyRevisionMatchesCreationTimestamp { get; init; }
    public bool HasInitialUtcOffsetProvenance { get; init; }
    public bool HasCompletionActivity { get; init; }
    public bool WasReopened { get; init; }
    public bool IsEligible { get; init; }
    public TaskDatasetExclusionReason ExclusionReason { get; init; }
    public required string ExclusionReasonCode { get; init; }
    public required string Analysis { get; init; }
}
