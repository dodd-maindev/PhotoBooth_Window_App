using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;
using Photobooth.Desktop.Models;

namespace Photobooth.Desktop.Services;

public sealed class CameraFolderWatcher : IDisposable
{
    private readonly string _watchFolder;
    private readonly string _processingFolder;
    private readonly FileLogger _logger;
    private readonly FileSystemWatcher _watcher;
    private readonly Channel<string> _incomingPaths = Channel.CreateUnbounded<string>();
    private readonly ConcurrentDictionary<string, byte> _debounce = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;

    public CameraFolderWatcher(string watchFolder, string processingFolder, FileLogger logger)
    {
        _watchFolder = watchFolder;
        _processingFolder = processingFolder;
        _logger = logger;

        Directory.CreateDirectory(_watchFolder);
        Directory.CreateDirectory(_processingFolder);

        _watcher = new FileSystemWatcher(_watchFolder)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = false,
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Renamed += OnRenamed;

        _loopTask = Task.Run(ProcessQueueAsync);
    }

    public event Func<CameraPhotoReadyEventArgs, Task>? PhotoReady;

    public void Start() => _watcher.EnableRaisingEvents = true;

    public void Stop() => _watcher.EnableRaisingEvents = false;

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsSupportedImage(e.FullPath))
        {
            return;
        }

        if (_debounce.TryAdd(e.FullPath, 0))
        {
            _ = _logger.InfoAsync($"Watcher event: {e.ChangeType} -> {e.FullPath}");
            _incomingPaths.Writer.TryWrite(e.FullPath);
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        OnFileChanged(sender, e);
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var path in _incomingPaths.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    await _logger.InfoAsync($"Waiting for file ready: {path}").ConfigureAwait(false);
                    var readyPath = await WaitForFileReadyAsync(path, _cts.Token).ConfigureAwait(false);
                    await _logger.InfoAsync($"File ready, copying: {readyPath}").ConfigureAwait(false);
                    var copiedPath = await CopyToProcessingFolderAsync(readyPath, _cts.Token).ConfigureAwait(false);

                    if (PhotoReady is not null)
                    {
                        await _logger.InfoAsync($"Raising PhotoReady for: {copiedPath}").ConfigureAwait(false);
                        await PhotoReady.Invoke(new CameraPhotoReadyEventArgs(readyPath, copiedPath)).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    await _logger.ErrorAsync($"Failed to stabilize incoming file {path}", ex).ConfigureAwait(false);
                }
                finally
                {
                    _debounce.TryRemove(path, out _);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsSupportedImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> WaitForFileReadyAsync(string path, CancellationToken cancellationToken)
    {
        const int attempts = 30;
        for (var i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0)
                {
                    throw new IOException("File has not been fully written yet.");
                }

                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                if (stream.Length > 0)
                {
                    return path;
                }
            }
            catch (IOException)
            {
            }

            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        throw new IOException($"Timed out waiting for file lock to release: {path}");
    }

    private async Task<string> CopyToProcessingFolderAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var destinationPath = Path.Combine(_processingFolder, $"{fileName}-{DateTime.Now:yyyyMMddHHmmssfff}{extension}");

        await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await using var destinationStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);

        await _logger.InfoAsync($"Copied new camera image {sourcePath} -> {destinationPath}").ConfigureAwait(false);
        return destinationPath;
    }

    public void Dispose()
    {
        Stop();
        _watcher.Dispose();
        _cts.Cancel();
        _incomingPaths.Writer.TryComplete();
        try
        {
            _loopTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cts.Dispose();
    }
}