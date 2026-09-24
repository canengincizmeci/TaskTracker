namespace TaskTracker.Core.Utilities.Results;

public sealed class ConflictResult : Result, IConflictResult
{
    public ConflictResult(string message) : base(false, message)
    {
    }
}
