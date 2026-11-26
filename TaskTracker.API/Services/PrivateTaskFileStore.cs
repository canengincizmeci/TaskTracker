using Microsoft.Extensions.Options;

namespace TaskTracker.API.Services;

public interface ITaskFileStore
{
    Task<string> SaveAsync(Stream source, CancellationToken cancellationToken);
    Stream OpenRead(string key);
    void Delete(string key);
}

public sealed class PrivateTaskFileStore(IOptions<TaskFileOptions> options, IWebHostEnvironment environment,
    ILogger<PrivateTaskFileStore>? logger = null) : ITaskFileStore
{
    private readonly string _root = Path.GetFullPath(Path.IsPathRooted(options.Value.RootPath)
        ? options.Value.RootPath : Path.Combine(environment.ContentRootPath, options.Value.RootPath));

    public async Task<string> SaveAsync(Stream source, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var key = Guid.NewGuid().ToString("N");
        var finalPath = Resolve(key);
        var temporaryPath = finalPath + ".upload";
        var created = false;
        try
        {
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                created = true;
                await source.CopyToAsync(destination, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, finalPath);
            return key;
        }
        catch
        {
            if (created)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger?.LogWarning(ex, "Could not remove a partial task upload");
                }
            }
            throw;
        }
    }

    public Stream OpenRead(string key) => new FileStream(Resolve(key), FileMode.Open, FileAccess.Read,
        FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
    public void Delete(string key) { var path = Resolve(key); if (File.Exists(path)) File.Delete(path); }
    private string Resolve(string key)
    {
        if (key.Length != 32 || key.Any(c => !Uri.IsHexDigit(c))) throw new InvalidOperationException("Invalid storage key.");
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid storage path.");
        return path;
    }
}
