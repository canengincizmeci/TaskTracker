namespace TaskTracker.Core.Utilities.Results;

public sealed class ConflictDataResult<T> : DataResult<T>, IConflictResult
{
    public ConflictDataResult(string message) : base(default!, false, message)
    {
    }
}
