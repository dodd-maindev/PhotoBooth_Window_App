using AForge.Video;
using AForge.Video.DirectShow;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Desktop.Services;

public sealed class CameraPreviewService : IDisposable
{
    private readonly FileLogger _logger;
    private readonly object _frameLock = new();
    private VideoCaptureDevice? _cameraDevice;
    private volatile bool _isStopping;
    private BitmapSource? _latestFrame;
    private DateTime? _lastFrameReceivedAtUtc;

    public CameraPreviewService(FileLogger logger)
    {
        _logger = logger;
    }

    public event Action<ImageSource>? FrameAvailable;

    public bool IsRunning { get; private set; }

    public string LastStatusMessage { get; private set; } = "";

    public bool TryGetLatestFrame(out BitmapSource? frame)
    {
        lock (_frameLock)
        {
            frame = _latestFrame;
            return frame is not null;
        }
    }

    public DateTime? LastFrameReceivedAtUtc
    {
        get
        {
            lock (_frameLock)
            {
                return _lastFrameReceivedAtUtc;
            }
        }
    }

    public async Task<string> CaptureLatestFrameAsync(string outputFolder, CancellationToken cancellationToken)
    {
        await _logger.InfoAsync($"Capture requested. Camera running={IsRunning}, hasFrame={TryGetLatestFrame(out _)}").ConfigureAwait(false);

        if (!TryGetLatestFrame(out var frame) || frame is null)
        {
            await _logger.WarnAsync($"Capture blocked: no live frame available. Camera running={IsRunning}, lastFrameUtc={LastFrameReceivedAtUtc:O}").ConfigureAwait(false);
            throw new InvalidOperationException("Chưa có frame camera để chụp.");
        }

        Directory.CreateDirectory(outputFolder);
        var fileName = $"capture-{DateTime.Now:yyyyMMdd-HHmmssfff}.png";
        var outputPath = Path.Combine(outputFolder, fileName);

        await using var fileStream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        encoder.Save(fileStream);

        await _logger.InfoAsync($"Captured live frame -> {outputPath}").ConfigureAwait(false);
        return outputPath;
    }

    public async Task<bool> StartAsync(string preferredCameraName, int deviceIndex, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return true;
        }

        var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
        ClearLatestFrame();
        if (devices.Count == 0)
        {
            LastStatusMessage = "Không tìm thấy camera video nào trên máy.";
            await _logger.WarnAsync(LastStatusMessage).ConfigureAwait(false);
            return false;
        }

        await _logger.InfoAsync("Detected video input devices:").ConfigureAwait(false);
        foreach (FilterInfo device in devices)
        {
            await _logger.InfoAsync($"- {device.Name}").ConfigureAwait(false);
        }

        var selectedDevice = SelectDevice(devices, preferredCameraName, deviceIndex);
        if (selectedDevice is null)
        {
            LastStatusMessage = $"Không tìm thấy camera phù hợp với '{preferredCameraName}'.";
            await _logger.WarnAsync(LastStatusMessage).ConfigureAwait(false);
            return false;
        }

        try
        {
            _cameraDevice = new VideoCaptureDevice(selectedDevice.MonikerString);
            _cameraDevice.NewFrame += OnNewFrame;
            _cameraDevice.PlayingFinished += OnPlayingFinished;
            _cameraDevice.Start();

            var started = await WaitForStartAsync(cancellationToken).ConfigureAwait(false);
            if (!started)
            {
                Stop();
                LastStatusMessage = $"Không mở được camera '{selectedDevice.Name}'.";
                await _logger.WarnAsync(LastStatusMessage).ConfigureAwait(false);
                return false;
            }

            IsRunning = true;
            // LastStatusMessage = $"Camera live view đang chạy: {selectedDevice.Name}";
            await _logger.InfoAsync(LastStatusMessage).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LastStatusMessage = $"Lỗi mở camera: {ex.Message}";
            await _logger.ErrorAsync(LastStatusMessage, ex).ConfigureAwait(false);
            Stop();
            return false;
        }
    }

    private static FilterInfo? SelectDevice(FilterInfoCollection devices, string preferredCameraName, int deviceIndex)
    {
        if (!string.IsNullOrWhiteSpace(preferredCameraName))
        {
            var preferred = devices.Cast<FilterInfo>()
                .FirstOrDefault(device => device.Name.Contains(preferredCameraName, StringComparison.OrdinalIgnoreCase));

            if (preferred is not null)
            {
                return preferred;
            }

            return null;
        }

        if (deviceIndex >= 0 && deviceIndex < devices.Count)
        {
            return devices[deviceIndex];
        }

        return devices.Cast<FilterInfo>().FirstOrDefault();
    }

    private async Task<bool> WaitForStartAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 50; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_cameraDevice is not null && _cameraDevice.IsRunning)
            {
                return true;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
    {
        if (_isStopping)
        {
            return;
        }

        try
        {
            using var frame = (Bitmap)eventArgs.Frame.Clone();
            var imageSource = ConvertBitmapToImageSource(frame);
            lock (_frameLock)
            {
                _latestFrame = imageSource;
                _lastFrameReceivedAtUtc = DateTime.UtcNow;
            }
            FrameAvailable?.Invoke(imageSource);
        }
        catch (Exception ex)
        {
            LastStatusMessage = $"Camera frame error: {ex.Message}";
            _ = _logger.ErrorAsync(LastStatusMessage, ex);
        }
    }

    private void OnPlayingFinished(object sender, ReasonToFinishPlaying reason)
    {
        if (_isStopping)
        {
            return;
        }

        LastStatusMessage = $"Camera stream đã dừng: {reason}";
        _ = _logger.WarnAsync(LastStatusMessage);
        IsRunning = false;
        ClearLatestFrame();
    }

    private void ClearLatestFrame()
    {
        lock (_frameLock)
        {
            _latestFrame = null;
            _lastFrameReceivedAtUtc = null;
        }
    }

    private static BitmapSource ConvertBitmapToImageSource(Bitmap bitmap)
    {
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Bmp);
        memoryStream.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = memoryStream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }

    public void Stop()
    {
        _isStopping = true;

        if (_cameraDevice is not null)
        {
            _cameraDevice.NewFrame -= OnNewFrame;
            _cameraDevice.PlayingFinished -= OnPlayingFinished;

            if (_cameraDevice.IsRunning)
            {
                _cameraDevice.SignalToStop();
                _cameraDevice.WaitForStop();
            }

            _cameraDevice = null;
        }

        IsRunning = false;
        ClearLatestFrame();
        _isStopping = false;
    }

    public void Dispose()
    {
        Stop();
    }
}