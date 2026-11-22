namespace TaskTracker.Core.Utilities.Enums;

public enum TaskDatasetExclusionReason
{
    None = 0,
    MissingInitialSnapshot = 1,
    Deleted = 2,
    MissingInitialDeadline = 3,
    ReopenedUnfinished = 4,
    ReopenedAmbiguousLifecycle = 5,
    Cancelled = 6,
    Unfinished = 7,
    IncompleteCompletionHistory = 8
}
