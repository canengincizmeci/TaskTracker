namespace TaskTracker.API.Services;

public sealed class TaskFileOptions
{
    public string RootPath { get; set; } = "App_Data/task-files";
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
    public bool RequireCleanScan { get; set; }
}
