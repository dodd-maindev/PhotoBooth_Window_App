using System.IO;
using System.Text;

namespace Photobooth.Desktop.Services;

public sealed class FileLogger
{
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileLogger(string logFolder)
    {
        Directory.CreateDirectory(logFolder);
        _logFilePath = Path.Combine(logFolder, $"photobooth-{DateTime.Now:yyyyMMdd}.log");
    }

    public Task InfoAsync(string message) => WriteAsync("INFO", message, null);

    public Task WarnAsync(string message) => WriteAsync("WARN", message, null);

    public Task DebugAsync(string message) => WriteAsync("DEBUG", message, null);

    public Task ErrorAsync(string message, Exception? exception = null) => WriteAsync("ERROR", message, exception);

    private async Task WriteAsync(string level, string message, Exception? exception)
    {
        var builder = new StringBuilder();
        builder.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
        builder.Append(level).Append(' ').Append(message);

        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append(exception);
        }

        builder.AppendLine();
        var logLine = builder.ToString();

        try
        {
            Console.Write(logLine);
        }
        catch
        {
        }

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_logFilePath, logLine).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}