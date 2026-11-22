namespace TaskTracker.API.Services;

public sealed class TaskFileOptions
{
    public string RootPath { get; set; } = "App_Data/task-files";
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
    public bool RequireCleanScan { get; set; } = true;
    public bool EnableSubTaskSubmissionUploads { get; set; }
    public bool EnableDiscussionUploads { get; set; } = true;
    public int MaxDiscussionAttachments { get; set; } = 5;
    public long MaxDiscussionUploadBytes { get; set; } = 40 * 1024 * 1024;
    public ClamAvScannerOptions Scanner { get; set; } = new();
}

public sealed class ClamAvScannerOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3310;
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int ScanTimeoutMs { get; set; } = 15000;
    public int MaxConcurrentScans { get; set; } = 4;
    public int ChunkSizeBytes { get; set; } = 64 * 1024;
}
