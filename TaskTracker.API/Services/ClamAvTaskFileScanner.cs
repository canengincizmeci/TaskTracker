using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaskTracker.API.Services;

public static class TaskFileServiceRegistration
{
    public static IServiceCollection AddTaskFileServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var section = configuration.GetSection("TaskFiles");
        services.Configure<TaskFileOptions>(section);
        services.AddSingleton<ITaskFileStore, PrivateTaskFileStore>();
        services.AddSingleton<TaskUploadSlots>();
        services.AddScoped<TaskUploadFilter>();

        var options = section.Get<TaskFileOptions>() ?? new TaskFileOptions();
        if (options.Scanner.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Scanner.Host) ||
                options.Scanner.Port is <= 0 or > 65535)
            {
                throw new InvalidOperationException(
                    "TaskFiles:Scanner is enabled, but TaskFiles:Scanner:Host or TaskFiles:Scanner:Port is invalid.");
            }

            if (environment.IsDevelopment() && !ClamAvTaskFileScanner.IsLoopbackHost(options.Scanner.Host))
            {
                throw new InvalidOperationException(
                    "In Development, TaskFiles:Scanner:Host must be restricted to localhost (127.0.0.1, ::1, or localhost).");
            }

            services.AddSingleton<ITaskFileScanner, ClamAvTaskFileScanner>();
        }
        else
        {
            services.AddSingleton<ITaskFileScanner, NoOpTaskFileScanner>();
        }

        return services;
    }
}

public sealed class ClamAvTaskFileScanner : ITaskFileScanner, IDisposable
{
    private static readonly ReadOnlyMemory<byte> InstreamCommand = "zINSTREAM\0"u8.ToArray();
    private static readonly ReadOnlyMemory<byte> PingCommand = "zPING\0"u8.ToArray();
    private const int MaxResponseBytes = 4096;
    private const int MinChunkSizeBytes = 1024;
    private const int MaxChunkSizeBytes = 256 * 1024;

    private readonly ClamAvScannerOptions _options;
    private readonly long _maxFileSizeBytes;
    private readonly ILogger<ClamAvTaskFileScanner>? _logger;
    private readonly SemaphoreSlim _concurrencyGate;

    public ClamAvTaskFileScanner(IOptions<TaskFileOptions> options, ILogger<ClamAvTaskFileScanner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var fileOptions = options.Value;
        _options = fileOptions.Scanner ?? new ClamAvScannerOptions();
        _maxFileSizeBytes = Math.Max(1, fileOptions.MaxFileSizeBytes);
        _logger = logger;
        var maxConcurrent = Math.Clamp(_options.MaxConcurrentScans, 1, 64);
        _concurrencyGate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public bool IsConfigured =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.Host) &&
        _options.Port is > 0 and <= 65535;

    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var trimmed = host.Trim();
        if (string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(trimmed, out var address) && IPAddress.IsLoopback(address);
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConfigured) return false;

        var connectTimeout = TimeSpan.FromMilliseconds(Math.Clamp(_options.ConnectTimeoutMs, 50, 60_000));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(connectTimeout);
        try
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(_options.Host, _options.Port, cts.Token);
            await using var network = client.GetStream();
            await network.WriteAsync(PingCommand, cts.Token);
            await network.FlushAsync(cts.Token);
            var reply = await ReadResponseAsync(network, cts.Token);
            return string.Equals(reply, "PONG", StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or TimeoutException)
        {
            return false;
        }
    }

    public async Task<TaskFileScanVerdict> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConfigured)
        {
            return TaskFileScanVerdict.NotConfigured;
        }

        if (content.CanSeek)
        {
            if (content.Length <= 0 || content.Length > _maxFileSizeBytes)
            {
                return TaskFileScanVerdict.NotConfigured;
            }
            content.Position = 0;
        }

        var connectTimeout = TimeSpan.FromMilliseconds(Math.Clamp(_options.ConnectTimeoutMs, 50, 60_000));
        var scanTimeout = TimeSpan.FromMilliseconds(Math.Clamp(_options.ScanTimeoutMs, 100, 300_000));
        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scanCts.CancelAfter(scanTimeout);
        var token = scanCts.Token;

        var acquired = false;
        try
        {
            await _concurrencyGate.WaitAsync(token);
            acquired = true;

            using var client = new TcpClient { NoDelay = true };
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                connectCts.CancelAfter(connectTimeout);
                await client.ConnectAsync(_options.Host, _options.Port, connectCts.Token);
            }

            await using var network = client.GetStream();
            await network.WriteAsync(InstreamCommand, token);

            var chunkSize = Math.Clamp(_options.ChunkSizeBytes, MinChunkSizeBytes, MaxChunkSizeBytes);
            var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
            var header = new byte[4];
            long totalSent = 0;
            try
            {
                while (true)
                {
                    var read = await content.ReadAsync(buffer.AsMemory(0, chunkSize), token);
                    if (read <= 0) break;

                    totalSent += read;
                    if (totalSent > _maxFileSizeBytes)
                    {
                        _logger?.LogWarning(
                            "ClamAV scan aborted because the stream exceeded the configured size ceiling ({MaxFileSizeBytes} bytes).",
                            _maxFileSizeBytes);
                        return TaskFileScanVerdict.NotConfigured;
                    }

                    BinaryPrimitives.WriteUInt32BigEndian(header, (uint)read);
                    await network.WriteAsync(header.AsMemory(), token);
                    await network.WriteAsync(buffer.AsMemory(0, read), token);
                }

                if (totalSent <= 0 || (content.CanSeek && totalSent != content.Length))
                {
                    _logger?.LogWarning("ClamAV scan aborted because the stream size was invalid or changed during streaming.");
                    return TaskFileScanVerdict.NotConfigured;
                }

                BinaryPrimitives.WriteUInt32BigEndian(header, 0u);
                await network.WriteAsync(header.AsMemory(), token);
                await network.FlushAsync(token);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var response = await ReadResponseAsync(network, token);
            var verdict = ParseVerdict(response);
            if (verdict == TaskFileScanVerdict.Clean && content.CanSeek)
            {
                content.Position = 0;
            }
            return verdict;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger?.LogWarning(ex, "ClamAV scan timed out talking to {Host}:{Port}.", _options.Host, _options.Port);
            return TaskFileScanVerdict.NotConfigured;
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
        {
            _logger?.LogWarning(ex, "ClamAV scanner at {Host}:{Port} could not complete the scan.", _options.Host, _options.Port);
            return TaskFileScanVerdict.NotConfigured;
        }
        finally
        {
            if (acquired)
            {
                _concurrencyGate.Release();
            }
        }
    }

    private static async Task<string?> ReadResponseAsync(Stream network, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxResponseBytes);
        try
        {
            var total = 0;
            while (total < MaxResponseBytes)
            {
                var read = await network.ReadAsync(buffer.AsMemory(total, MaxResponseBytes - total), token);
                if (read <= 0)
                {
                    return null;
                }

                var span = buffer.AsSpan(total, read);
                var terminatorIndex = span.IndexOfAny((byte)'\0', (byte)'\n');
                if (terminatorIndex >= 0)
                {
                    var remainder = span[(terminatorIndex + 1)..];
                    foreach (var extra in remainder)
                    {
                        if (extra is not ((byte)'\0' or (byte)'\r' or (byte)'\n'))
                        {
                            return null;
                        }
                    }

                    var length = total + terminatorIndex;
                    return Encoding.ASCII.GetString(buffer, 0, length).TrimEnd('\r', ' ');
                }

                total += read;
            }

            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private TaskFileScanVerdict ParseVerdict(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            _logger?.LogWarning("ClamAV returned an empty or unterminated scan response.");
            return TaskFileScanVerdict.NotConfigured;
        }

        if (string.Equals(response, "stream: OK", StringComparison.Ordinal))
        {
            return TaskFileScanVerdict.Clean;
        }

        const string prefix = "stream: ";
        const string foundSuffix = " FOUND";
        if (response.StartsWith(prefix, StringComparison.Ordinal) &&
            response.EndsWith(foundSuffix, StringComparison.Ordinal) &&
            response.Length > prefix.Length + foundSuffix.Length)
        {
            var signature = response.AsSpan(prefix.Length, response.Length - prefix.Length - foundSuffix.Length).Trim();
            if (!signature.IsEmpty && !signature.ContainsAny('\0', '\r', '\n'))
            {
                _logger?.LogWarning("ClamAV rejected an upload matching threat signature {Signature}.", signature.ToString());
                return TaskFileScanVerdict.Rejected;
            }
        }

        _logger?.LogWarning("ClamAV returned a non-clean or unexpected response.");
        return TaskFileScanVerdict.NotConfigured;
    }

    public void Dispose() => _concurrencyGate.Dispose();
}
