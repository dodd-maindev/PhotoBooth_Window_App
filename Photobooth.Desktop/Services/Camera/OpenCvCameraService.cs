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
    /// Also disables auto-exposure and sets manual exposure/brightness to reduce glare.
    /// </summary>
    private static void ApplyCameraSettings(VideoCapture capture)
    {
        // MJPG must be set BEFORE resolution/fps or many cameras ignore it.
        capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
        capture.Set(VideoCaptureProperties.FrameWidth, TargetWidth);
        capture.Set(VideoCaptureProperties.FrameHeight, TargetHeight);
        capture.Set(VideoCaptureProperties.Fps, TargetFps);

        // --- Disable auto-exposure at hardware level ---
        // 0.0 = manual exposure mode on most DirectShow cameras
        // 2.0 = manual mode on some cameras, -1 = powered off
        capture.Set(VideoCaptureProperties.AutoExposure, 0.0);

        // --- Set manual exposure to minimum (darkest) ---
        // OpenCV exposure scale is typically 1-5000 for logarithmic scale
        // Lower value = less exposure = darker
        capture.Set(VideoCaptureProperties.Exposure, 2);

        // --- Reduce brightness at hardware level ---
        // Lower values = darker, typical range 0-255
        capture.Set(VideoCaptureProperties.Brightness, 10);

        // --- Minimize gain (causes noise when high) ---
        capture.Set(VideoCaptureProperties.Gain, 0);

        // --- Reduce contrast to help with glare ---
        capture.Set(VideoCaptureProperties.Contrast, 64);

        // --- Reduce saturation ---
        capture.Set(VideoCaptureProperties.Saturation, 50);

        // Minimise internal driver buffer — reduces latency at cost of 1 frame.
        capture.Set(VideoCaptureProperties.BufferSize, 1);
    }

    /// <summary>Logs the actual resolution, FPS, and camera control values.</summary>
    private async Task LogActualCameraSettings(VideoCapture capture)
    {
        var actualWidth = capture.Get(VideoCaptureProperties.FrameWidth);
        var actualHeight = capture.Get(VideoCaptureProperties.FrameHeight);
        var actualFps = capture.Get(VideoCaptureProperties.Fps);
        var actualExposure = capture.Get(VideoCaptureProperties.Exposure);
        var actualBrightness = capture.Get(VideoCaptureProperties.Brightness);
        var actualGain = capture.Get(VideoCaptureProperties.Gain);
        await _logger.InfoAsync(
            $"[OpenCv] Actual camera: {actualWidth}x{actualHeight} @ {actualFps} fps | Exposure={actualExposure} Brightness={actualBrightness} Gain={actualGain}")
            .ConfigureAwait(false);
    }

    // ── Private: Capture loop ──────────────────────────────────────────────
    /// <summary>
    /// Hot-loop running on a dedicated thread. Reads frames from the driver
    /// and dispatches them to the UI at the target frame rate.
    /// Applies strong brightness reduction and edge-preserving smoothing
    /// to eliminate glare/hot-spots from LED ring lights.
    /// </summary>
    private void CaptureLoop(CancellationToken ct)
    {
        using var frame = new Mat();
        using var bgrFrame = new Mat();

        // Brightness factor: 0.08 = reduce to 8% brightness (very dark)
        // Adjust this value (0.05 to 0.3) to get desired darkness
        const float BrightnessFactor = 0.08f;

        // Gamma value: 2.5 = darkens shadows more than highlights
        // Adjust (1.5 to 4.0) to control shadow darkness
        const double Gamma = 2.5;

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

                // Throttle UI dispatches
                var nowTicks = DateTime.UtcNow.Ticks;
                if (nowTicks - Interlocked.Read(ref _lastDispatchedTicksUtc) < DispatchIntervalTicks)
                    continue;

                // Convert to BGR first
                Cv2.CvtColor(frame, bgrFrame, ColorConversionCodes.BGRA2BGR);

                // Step 1: Apply gamma correction FIRST (darkens shadows more than highlights)
                // This makes the dark areas darker while preserving some mid-tones
                ApplyGammaCorrection(bgrFrame, Gamma);

                // Get raw pixel data after gamma
                var pixelData = new byte[bgrFrame.Width * bgrFrame.Height * 3];
                Marshal.Copy(bgrFrame.Data, pixelData, 0, pixelData.Length);

                // Step 2: Direct pixel manipulation - multiply by brightness factor
                // This reduces ALL pixel values uniformly
                for (int i = 0; i < pixelData.Length; i++)
                {
                    pixelData[i] = (byte)(pixelData[i] * BrightnessFactor);
                }

                // Create darkened Mat from modified pixels
                using var darkenedMat = new Mat(bgrFrame.Height, bgrFrame.Width, MatType.CV_8UC3);
                Marshal.Copy(pixelData, 0, darkenedMat.Data, pixelData.Length);

                // Step 3: Apply bilateral filter to reduce noise while keeping edges
                // This smooths flat areas (removes grain from darkening) without blurring edges
                using var smoothed = new Mat();
                Cv2.BilateralFilter(darkenedMat, smoothed, 9, 75, 75);

                // Step 4: Convert to BGRA for display
                using var bgraResult = new Mat();
                Cv2.CvtColor(smoothed, bgraResult, ColorConversionCodes.BGR2BGRA);

                DispatchFrame(bgraResult, nowTicks);
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

    /// <summary>
    /// Applies gamma correction (darkens shadows more than highlights) to a BGR Mat.
    /// gamma > 1: darkens image, gamma &lt; 1: brightens image.
    /// Uses HSV conversion for better control over brightness.
    /// </summary>
    private static void ApplyGammaCorrection(Mat bgrFrame, double gamma)
    {
        // Convert BGR to HSV (Hue, Saturation, Value)
        using var hsv = new Mat();
        Cv2.CvtColor(bgrFrame, hsv, ColorConversionCodes.BGR2HSV);

        // Split channels into separate Mats
        using var hChannel = new Mat();
        using var sChannel = new Mat();
        using var vChannel = new Mat();
        Cv2.Split(hsv, out var hsvChannels);

        // Apply gamma to V channel (brightness)
        // V_new = 255 * (V_old / 255) ^ gamma
        var vData = new byte[hsvChannels[2].Width * hsvChannels[2].Height];
        Marshal.Copy(hsvChannels[2].Data, vData, 0, vData.Length);
        for (int i = 0; i < vData.Length; i++)
        {
            vData[i] = (byte)(Math.Pow(vData[i] / 255.0, gamma) * 255.0);
        }
        Marshal.Copy(vData, 0, hsvChannels[2].Data, vData.Length);

        // Merge channels back
        Cv2.Merge(hsvChannels, hsv);

        // Convert back to BGR
        Cv2.CvtColor(hsv, bgrFrame, ColorConversionCodes.HSV2BGR);
    }

    /// <summary>
    /// Builds a gamma correction lookup table for a single channel.
    /// gamma > 1: darkens, gamma &lt; 1: brightens.
    /// </summary>
    private static byte[] BuildGammaLutTable(double gamma)
    {
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var val = Math.Pow(i / 255.0, gamma) * 255.0;
            lut[i] = (byte)Math.Clamp(val, 0, 255);
        }
        return lut;
    }

    /// <summary>
    /// Applies gamma correction using lookup table to each BGR channel.
    /// gamma > 1: darkens image, gamma &lt; 1: brightens image.
    /// </summary>
    private static void ApplyGammaLutBgr(Mat bgrFrame, byte[] lut)
    {
        Cv2.Split(bgrFrame, out var channels);

        for (int c = 0; c < 3; c++)
        {
            var data = new byte[channels[c].Width * channels[c].Height];
            Marshal.Copy(channels[c].Data, data, 0, data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = lut[data[i]];
            }
            Marshal.Copy(data, 0, channels[c].Data, data.Length);
        }

        Cv2.Merge(channels, bgrFrame);
    }
}
