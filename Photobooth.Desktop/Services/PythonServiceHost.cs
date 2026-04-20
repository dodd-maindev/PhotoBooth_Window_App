using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Photobooth.Desktop.Services;

/// <summary>
/// Manages the lifecycle of the bundled Python FastAPI service process.
///
/// On startup, extracts the embedded python_service.exe to a temporary
/// directory, spawns it as a hidden child process, then polls the /health
/// endpoint until the service is ready. On shutdown, kills the process
/// and cleans up the temporary directory.
/// </summary>
public sealed class PythonServiceHost : IDisposable
{
    private const string EmbeddedResourceName =
        "Photobooth.Desktop.Resources.python_service.exe";

    private const string HealthEndpoint = "http://127.0.0.1:8000/health";
    private const int HealthPollIntervalMs = 500;
    private const int StartupTimeoutSeconds = 60;

    private readonly string _tempDirectory;
    private Process? _serviceProcess;
    private bool _disposed;

    /// <summary>Initialises the host and prepares the temporary work directory.</summary>
    public PythonServiceHost()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "photobooth_svc");
    }

    /// <summary>
    /// Extracts and starts the Python service, then waits until it is healthy.
    /// If the embedded resource is not found (e.g. during development), the
    /// method returns immediately so the developer can run the service manually.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the startup wait.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var executablePath = ExtractEmbeddedExecutable();
        if (executablePath is null)
        {
            Console.WriteLine("[PythonServiceHost] Embedded exe not found — assuming dev mode.");
            return;
        }

        SpawnProcess(executablePath);
        await WaitUntilHealthyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Kills the Python service process and removes the temp directory.</summary>
    public void Stop()
    {
        if (_serviceProcess is null || _serviceProcess.HasExited)
            return;

        try
        {
            _serviceProcess.Kill(entireProcessTree: true);
            _serviceProcess.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PythonServiceHost] Error stopping process: {ex.Message}");
        }
        finally
        {
            _serviceProcess.Dispose();
            _serviceProcess = null;
            CleanupTempDirectory();
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Reads the embedded python_service.exe from the assembly manifest and
    /// writes it to the temporary directory. Returns null if not embedded
    /// (development mode without a prior PyInstaller build).
    /// </summary>
    private string? ExtractEmbeddedExecutable()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
            return null;

        Directory.CreateDirectory(_tempDirectory);
        var destPath = Path.Combine(_tempDirectory, "python_service.exe");

        using var fileStream = File.Create(destPath);
        stream.CopyTo(fileStream);

        Console.WriteLine($"[PythonServiceHost] Extracted exe to: {destPath}");
        return destPath;
    }

    /// <summary>Starts python_service.exe as a hidden background process.</summary>
    private void SpawnProcess(string executablePath)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        _serviceProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start python_service.exe");

        Console.WriteLine($"[PythonServiceHost] Service started (PID {_serviceProcess.Id})");
    }

    /// <summary>Polls the /health endpoint until a 200 response is received.</summary>
    private static async Task WaitUntilHealthyAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(StartupTimeoutSeconds);

        Console.WriteLine("[PythonServiceHost] Waiting for service to become healthy...");

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await client.GetAsync(HealthEndpoint, cancellationToken)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("[PythonServiceHost] Service is healthy ✓");
                    return;
                }
            }
            catch { /* not ready yet */ }

            await Task.Delay(HealthPollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Python service did not become healthy within {StartupTimeoutSeconds}s.");
    }

    /// <summary>Removes the temporary directory used by the extracted executable.</summary>
    private void CleanupTempDirectory()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PythonServiceHost] Failed to clean temp dir: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
