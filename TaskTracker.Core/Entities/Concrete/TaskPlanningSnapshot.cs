using TaskTracker.Core.Utilities.Enums;

namespace TaskTracker.Core.Entities.Concrete;

/// <summary>
/// Immutable initial planning snapshot captured atomically when a task is created.
/// Stores only structured prediction-time features without sensitive text or personal identifiers.
/// </summary>
public class TaskPlanningSnapshot : IEntity
{
    public const string CurrentSchemaVersion = "1.0";
    public const int MaxSchemaVersionLength = 20;

    public int Id { get; init; }
    public int TaskRequestId { get; init; }
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public int InitialUtcOffsetMinutes { get; init; }
    public TaskPriority InitialPriority { get; init; }
    public DateOnly? InitialDueDate { get; init; }
    public DateTime? OriginalDueAtUtc { get; init; }
    public int? InitialPlannedCalendarDays { get; init; }
    public long? InitialPlannedDurationSeconds { get; init; }
    public TaskInitialAssignmentState InitialAssignmentState { get; init; }
    public bool IsWorkspaceTask { get; init; }
    public TaskVisibility InitialVisibility { get; init; }
    public int InitialTitleLength { get; init; }
    public int InitialDescriptionLength { get; init; }
    public TaskRequest TaskRequest { get; init; } = null!;

    /// <summary>
    /// Interprets an initial calendar deadline date as the end of that local calendar day
    /// (23:59:59.999999 in the creator's captured UTC offset) converted to UTC.
    /// Uses microsecond resolution so values round-trip identically across .NET, SQLite and PostgreSQL timestamptz.
    /// </summary>
    public static DateTime ComputeOriginalDueAtUtc(DateOnly initialDueDate, int initialUtcOffsetMinutes) =>
        initialDueDate
            .AddDays(1)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .AddMinutes(-initialUtcOffsetMinutes)
            .AddMicroseconds(-1);
}
