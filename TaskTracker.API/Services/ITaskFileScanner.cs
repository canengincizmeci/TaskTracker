namespace TaskTracker.API.Services;

public enum TaskFileScanVerdict { NotConfigured, Clean, Rejected }

public interface ITaskFileScanner
{
    Task<TaskFileScanVerdict> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken);
}

/// <summary>Explicit development boundary. Production can replace this service with a malware scanner.</summary>
public sealed class NoOpTaskFileScanner : ITaskFileScanner
{
    public Task<TaskFileScanVerdict> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
        Task.FromResult(TaskFileScanVerdict.NotConfigured);
}
