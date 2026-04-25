using System.Windows.Media;

namespace Photobooth.Desktop.Services.Camera;

public interface ICameraService : IDisposable
{
    bool IsRunning { get; }
    string LastStatusMessage { get; }
    DateTime? LastFrameReceivedAtUtc { get; }
    event Action<ImageSource>? FrameAvailable;
    event Func<string, Task>? PhotoCaptured;
    Task<bool> StartAsync(CancellationToken cancellationToken);
    Task<string> CaptureLatestFrameAsync(string outputFolder, CancellationToken cancellationToken);
    void Stop();
    bool TryGetLatestFrame(out ImageSource? frame);
}
