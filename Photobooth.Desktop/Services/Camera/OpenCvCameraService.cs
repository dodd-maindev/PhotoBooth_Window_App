using OpenCvSharp;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Desktop.Services.Camera;

/// <summary>
/// OpenCV-based webcam live-preview service.
/// Forces 1080p @ 60 fps via MJPG codec and dispatches frames
/// to the UI at the full 60 fps rate using a reused WriteableBitmap
/// to minimise GC pressure.
/// </summary>
public sealed class OpenCvCameraService : ICameraService
{
    // ── Constants ──────────────────────────────────────────────────────────
    /// <summary>640x480 is the standard low-res mode that allows webcams to push
    /// the highest possible frame rate over USB.</summary>
    private const int TargetWidth = 640;
    private const int TargetHeight = 480;
    private const double TargetFps = 240.0;

    /// <summary>4.17 ms expressed in 100-ns ticks → true 240 fps dispatch.</summary>
    private const long DispatchIntervalTicks = 41_667;

    // ── Fields ─────────────────────────────────────────────────────────────
    private readonly FileLogger _logger;
    private readonly string _preferredCameraName;
    private readonly int _deviceIndex;
    private readonly object _frameLock = new();

    private VideoCapture? _capture;
    private CancellationTokenSource? _captureCts;
    private Task? _captureLoop;
    private volatile bool _isCapturing;
    private volatile bool _isStopping;

    private WriteableBitmap? _sharedBitmap;
    private BitmapSource? _latestFrame;
    private DateTime? _lastFrameReceivedAtUtc;
    private long _lastDispatchedTicksUtc;

    // ── Constructor ────────────────────────────────────────────────────────
    /// <summary>Initialises a new camera service instance.</summary>
    public OpenCvCameraService(FileLogger logger, string preferredCameraName, int deviceIndex)
    {
        _logger = logger;
        _preferredCameraName = preferredCameraName;
        _deviceIndex = deviceIndex;
    }

    // ── Events & Properties ────────────────────────────────────────────────
    public event Action<ImageSource>? FrameAvailable;
    public event Func<string, Task>? PhotoCaptured;

    public bool IsRunning { get; private set; }
    public string LastStatusMessage { get; private set; } = string.Empty;

    public DateTime? LastFrameReceivedAtUtc
    {
        get { lock (_frameLock) { return _lastFrameReceivedAtUtc; } }
    }

    // ── Public API ─────────────────────────────────────────────────────────
    /// <inheritdoc/>
    public bool TryGetLatestFrame(out ImageSource? frame)
    {
        lock (_frameLock) { frame = _latestFrame; return frame is not null; }
    }

    /// <inheritdoc/>
    public async Task<string> CaptureLatestFrameAsync(
        string outputFolder, CancellationToken cancellationToken)
    {
        await _logger.InfoAsync(
            "[OpenCv] Capture requested. running=" + IsRunning +
            " hasFrame=" + TryGetLatestFrame(out _)).ConfigureAwait(false);

        if (!TryGetLatestFrame(out var rawFrame) || rawFrame is not BitmapSource bmpFrame)
        {
            await _logger.WarnAsync("[OpenCv] Capture blocked: no live frame.").ConfigureAwait(false);
            throw new InvalidOperationException("Chua co frame camera de chup.");
        }

        Directory.CreateDirectory(outputFolder);
        var fileName = "capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".png";
        var outputPath = Path.Combine(outputFolder, fileName);

        await using var fileStream = new FileStream(
            outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmpFrame));
        encoder.Save(fileStream);

        await _logger.InfoAsync("[OpenCv] Captured -> " + outputPath).ConfigureAwait(false);
        return outputPath;
    }

    /// <inheritdoc/>
    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) return true;

        var deviceIndex = ResolveDeviceIndex();
        await _logger.InfoAsync(
            "[OpenCv] Trying device " + deviceIndex +
            " (preferred: '" + _preferredCameraName + "')").ConfigureAwait(false);

        try
        {
            _capture = OpenDevice(deviceIndex, cancellationToken);
            if (_capture is null)
            {
                LastStatusMessage = "Khong mo duoc camera video nao.";
                await _logger.WarnAsync("[OpenCv] " + LastStatusMessage).ConfigureAwait(false);
                return false;
            }

            ApplyCameraSettings(_capture);
            await LogActualCameraSettings(_capture).ConfigureAwait(false);

            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isCapturing = true;
            _captureLoop = Task.Run(
                () => CaptureLoop(_captureCts.Token), _captureCts.Token);

            IsRunning = true;
            LastStatusMessage = "OpenCV camera started: device " + deviceIndex;
            await _logger.InfoAsync("[OpenCv] " + LastStatusMessage).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LastStatusMessage = "Loi khoi dong camera: " + ex.Message;
            await _logger.ErrorAsync("[OpenCv] " + LastStatusMessage, ex).ConfigureAwait(false);
            Stop();
            return false;
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        _isStopping = true;
        _isCapturing = false;

        _captureCts?.Cancel();
        _captureCts?.Dispose();
        _captureCts = null;

        try { _captureLoop?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        _captureLoop = null;

        try { _capture?.Release(); } catch { }
        _capture?.Dispose();
        _capture = null;

        IsRunning = false;
        ClearLatestFrame();
        _isStopping = false;
    }

    public void Dispose() => Stop();

    // ── Private: Device setup ──────────────────────────────────────────────
    /// <summary>
    /// Opens the target device index; falls back to any available device.
    /// </summary>
    private VideoCapture? OpenDevice(int preferredIndex, CancellationToken ct)
    {
        var cap = new VideoCapture(preferredIndex, VideoCaptureAPIs.DSHOW);
        if (cap.IsOpened()) return cap;

        cap.Dispose();
        for (var i = 0; i < 10; i++)
        {
            if (ct.IsCancellationRequested) return null;
            var fallback = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
            if (fallback.IsOpened()) return fallback;
            fallback.Dispose();
        }

        return null;
    }

    /// <summary>
    /// Applies 1080p @ 60 fps settings using MJPG codec (required by most
    /// webcams to unlock high-fps modes; raw YUY2 is typically limited to 30 fps).
    /// </summary>
    private static void ApplyCameraSettings(VideoCapture capture)
    {
        // MJPG must be set BEFORE resolution/fps or many cameras ignore it.
        capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
        capture.Set(VideoCaptureProperties.FrameWidth, TargetWidth);
        capture.Set(VideoCaptureProperties.FrameHeight, TargetHeight);
        capture.Set(VideoCaptureProperties.Fps, TargetFps);

        // Disable auto-exposure lag (DirectShow cameras).
        capture.Set(VideoCaptureProperties.AutoExposure, 0.25);

        // Minimise internal driver buffer — reduces latency at cost of 1 frame.
        capture.Set(VideoCaptureProperties.BufferSize, 1);
    }

    /// <summary>Logs the actual resolution and FPS negotiated by the driver.</summary>
    private async Task LogActualCameraSettings(VideoCapture capture)
    {
        var actualWidth = capture.Get(VideoCaptureProperties.FrameWidth);
        var actualHeight = capture.Get(VideoCaptureProperties.FrameHeight);
        var actualFps = capture.Get(VideoCaptureProperties.Fps);
        await _logger.InfoAsync(
            $"[OpenCv] Actual camera settings: {actualWidth}x{actualHeight} @ {actualFps} fps")
            .ConfigureAwait(false);
    }

    // ── Private: Capture loop ──────────────────────────────────────────────
    /// <summary>
    /// Hot-loop running on a dedicated thread.  Reads frames from the driver
    /// and dispatches them to the UI at the target frame rate.
    /// </summary>
    private void CaptureLoop(CancellationToken ct)
    {
        using var frame = new Mat();
        using var rgbFrame = new Mat();

        while (!ct.IsCancellationRequested && _isCapturing)
        {
            try
            {
                if (_capture is null || !_capture.IsOpened()) break;
                if (!_capture.Read(frame) || frame.Empty())
                {
                    if (ct.IsCancellationRequested) break;
                    Thread.Sleep(2);
                    continue;
                }

                if (_isStopping) break;

                // Throttle UI dispatches to TargetFps; frames in-between are
                // still read from the driver to keep its internal queue empty.
                var nowTicks = DateTime.UtcNow.Ticks;
                if (nowTicks - Interlocked.Read(ref _lastDispatchedTicksUtc) < DispatchIntervalTicks)
                    continue;

                Cv2.CvtColor(frame, rgbFrame, ColorConversionCodes.BGR2BGRA);
                DispatchFrame(rgbFrame, nowTicks);
            }
            catch (AccessViolationException ex)
            {
                _logger.ErrorAsync("[OpenCv] AccessViolation: " + ex.Message, ex).Wait();
                break;
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.ErrorAsync("[OpenCv] Frame error: " + ex.Message, ex).Wait();
            }
        }
    }

    /// <summary>
    /// Writes pixel data into the shared <see cref="WriteableBitmap"/> on the
    /// UI thread, avoiding per-frame heap allocations.
    /// </summary>
    private void DispatchFrame(Mat rgbFrame, long nowTicks)
    {
        var width = rgbFrame.Width;
        var height = rgbFrame.Height;
        var stride = width * 4;
        var pixelCount = height * stride;
        var pixels = new byte[pixelCount];
        Marshal.Copy(rgbFrame.Data, pixels, 0, pixelCount);

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // Lazily create or recreate the shared bitmap when dimensions change.
            if (_sharedBitmap is null
                || _sharedBitmap.PixelWidth != width
                || _sharedBitmap.PixelHeight != height)
            {
                _sharedBitmap = new WriteableBitmap(
                    width, height, 96, 96, PixelFormats.Bgra32, null);
            }

            _sharedBitmap.Lock();
            _sharedBitmap.WritePixels(
                new Int32Rect(0, 0, width, height), pixels, stride, 0);
            _sharedBitmap.Unlock();

            lock (_frameLock)
            {
                _latestFrame = _sharedBitmap;
                _lastFrameReceivedAtUtc = DateTime.UtcNow;
            }

            Interlocked.Exchange(ref _lastDispatchedTicksUtc, nowTicks);
            FrameAvailable?.Invoke(_sharedBitmap);
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    // ── Private: Helpers ───────────────────────────────────────────────────
    private int ResolveDeviceIndex()
    {
        if (!string.IsNullOrWhiteSpace(_preferredCameraName))
        {
            var candidates = EnumerateCameras();
            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Contains(_preferredCameraName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return _deviceIndex >= 0 ? _deviceIndex : 0;
    }

    private static List<string> EnumerateCameras()
    {
        var result = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            try
            {
                using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                if (cap.IsOpened()) result.Add("device-" + i);
            }
            catch { }
        }

        return result;
    }

    private void ClearLatestFrame()
    {
        lock (_frameLock)
        {
            _latestFrame = null;
            _lastFrameReceivedAtUtc = null;
        }
    }
}
