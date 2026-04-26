using OpenCvSharp;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Desktop.Services.Camera;

public sealed class CanonCameraService : ICameraService
{
    private readonly FileLogger _logger;
    private readonly string _preferredCameraName;
    private readonly int _deviceIndex;
    private readonly object _liveFrameLock = new();
    private VideoCapture? _liveCapture;
    private CancellationTokenSource? _liveCaptureCts;
    private Task? _liveCaptureLoop;
    private volatile bool _isCapturing;
    private volatile bool _isStopping;
    private WriteableBitmap? _sharedBitmap;
    private BitmapSource? _latestFrame;
    private DateTime? _lastFrameReceivedAtUtc;
    private long _lastDispatchedTicksUtc;
    private int _actualFpsCounter;
    private long _fpsWindowStartTicks;

    public CanonCameraService(FileLogger logger, string preferredCameraName, int deviceIndex)
    {
        _logger = logger;
        _preferredCameraName = preferredCameraName;
        _deviceIndex = deviceIndex;
    }

    public event Action<ImageSource>? FrameAvailable;
    public event Func<string, Task>? PhotoCaptured;

    public bool IsRunning { get; private set; }
    public string LastStatusMessage { get; private set; } = string.Empty;

    public DateTime? LastFrameReceivedAtUtc
    {
        get { lock (_liveFrameLock) { return _lastFrameReceivedAtUtc; } }
    }

    public bool TryGetLatestFrame(out ImageSource? frame)
    {
        lock (_liveFrameLock) { frame = _latestFrame; return frame is not null; }
    }

    public async Task<string> CaptureLatestFrameAsync(string outputFolder, CancellationToken cancellationToken)
    {
        await _logger.WarnAsync("[Canon] Bỏ qua nút Capture trên app. Yêu cầu bấm nút vật lý trên máy ảnh.").ConfigureAwait(false);
        throw new InvalidOperationException("Vui lòng bấm nút chụp trực tiếp trên máy ảnh Canon để nháy flash. App sẽ tự động nhận ảnh.");
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) return true;

        var deviceIndex = ResolveDeviceIndex();
        await _logger.InfoAsync("[Canon] Starting live preview on device index: " + deviceIndex).ConfigureAwait(false);

        try
        {
            _liveCapture = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);

            if (!_liveCapture.IsOpened())
            {
                await _logger.WarnAsync("[Canon] Cannot open device index " + deviceIndex + ". Trying fallback devices...").ConfigureAwait(false);
                _liveCapture.Dispose();
                _liveCapture = null;

                for (var i = 0; i < 3; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return false;
                    if (i == deviceIndex) continue;
                    try
                    {
                        var fallback = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                        if (fallback.IsOpened())
                        {
                            _liveCapture = fallback;
                            deviceIndex = i;
                            await _logger.InfoAsync("[Canon] Opened live preview fallback device index: " + i).ConfigureAwait(false);
                            break;
                        }
                        fallback.Dispose();
                    }
                    catch { }
                }

                if (_liveCapture is null || !_liveCapture.IsOpened())
                {
                    LastStatusMessage = "Khong mo duoc camera video nao.";
                    await _logger.WarnAsync("[Canon] " + LastStatusMessage).ConfigureAwait(false);
                    return false;
                }
            }

            // 640x480 @ 30fps - same as Fuji for smooth preview performance
            _liveCapture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
            _liveCapture.Set(VideoCaptureProperties.FrameWidth, 640);
            _liveCapture.Set(VideoCaptureProperties.FrameHeight, 480);
            _liveCapture.Set(VideoCaptureProperties.Fps, 30);
            _liveCapture.Set(VideoCaptureProperties.BufferSize, 1);

            // Log actual settings
            var actualWidth = _liveCapture.Get(VideoCaptureProperties.FrameWidth);
            var actualHeight = _liveCapture.Get(VideoCaptureProperties.FrameHeight);
            var actualFps = _liveCapture.Get(VideoCaptureProperties.Fps);
            await _logger.InfoAsync($"[Canon] Live webcam actual settings: {actualWidth}x{actualHeight} @ {actualFps} fps").ConfigureAwait(false);

            _liveCaptureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isCapturing = true;
            _liveCaptureLoop = Task.Run(() => LiveCaptureLoop(_liveCaptureCts.Token), _liveCaptureCts.Token);

            IsRunning = true;
            LastStatusMessage = "Canon live view: device " + deviceIndex;
            await _logger.InfoAsync("[Canon] " + LastStatusMessage).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LastStatusMessage = "Loi khoi dong camera Canon: " + ex.Message;
            await _logger.ErrorAsync("[Canon] " + LastStatusMessage, ex).ConfigureAwait(false);
            Stop();
            return false;
        }
    }

    private int ResolveDeviceIndex()
    {
        if (_deviceIndex >= 0)
        {
            try
            {
                using var cap = new VideoCapture(_deviceIndex, VideoCaptureAPIs.DSHOW);
                if (cap.IsOpened())
                {
                    _logger.InfoAsync($"[Canon] Direct connect to device index {_deviceIndex}").Wait();
                    return _deviceIndex;
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(_preferredCameraName))
        {
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                    if (cap.IsOpened())
                    {
                        var name = "device-" + i;
                        if (name.Contains(_preferredCameraName, StringComparison.OrdinalIgnoreCase))
                            return i;
                    }
                }
                catch { }
            }
        }

        try
        {
            using var cap = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
            if (cap.IsOpened())
                return 0;
        }
        catch { }

        for (var i = 1; i < 5; i++)
        {
            try
            {
                using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                if (cap.IsOpened())
                    return i;
            }
            catch { }
        }

        return 0;
    }

    private void LiveCaptureLoop(CancellationToken ct)
    {
        using var frame = new Mat();
        using var rgbFrame = new Mat();
        // Dispatch at ~24 fps (same as Fuji for smooth preview)
        const long DispatchIntervalTicks = 41_667; // 4.17ms in 100ns ticks

        while (!ct.IsCancellationRequested && _isCapturing)
        {
            try
            {
                if (_liveCapture is null || !_liveCapture.IsOpened())
                    break;

                if (!_liveCapture.Read(frame) || frame.Empty())
                {
                    if (ct.IsCancellationRequested) break;
                    Thread.Sleep(5);
                    continue;
                }

                if (_isStopping) break;

                var nowTicks = DateTime.UtcNow.Ticks;
                if (nowTicks - Interlocked.Read(ref _lastDispatchedTicksUtc) < DispatchIntervalTicks)
                    continue; // Drop frame - save UI thread bandwidth

                Cv2.CvtColor(frame, rgbFrame, ColorConversionCodes.BGR2BGRA);
                DispatchFrame(rgbFrame, nowTicks);
                MeasureAndLogActualFps(nowTicks);
            }
            catch (AccessViolationException ex)
            {
                _logger.ErrorAsync("[Canon] AccessViolation in live capture loop: " + ex.Message, ex).Wait();
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.ErrorAsync("[Canon] Live frame capture error: " + ex.Message, ex).Wait();
            }
        }
    }

    /// <summary>
    /// Writes pixel data into the shared WriteableBitmap on the UI thread,
    /// avoiding per-frame heap allocations. Same logic as Fuji.
    /// </summary>
    private void DispatchFrame(Mat rgbFrame, long nowTicks)
    {
        var width = rgbFrame.Width;
        var height = rgbFrame.Height;
        var stride = width * 4;
        var pixels = new byte[height * stride];
        Marshal.Copy(rgbFrame.Data, pixels, 0, pixels.Length);

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_sharedBitmap is null
                || _sharedBitmap.PixelWidth != width
                || _sharedBitmap.PixelHeight != height)
            {
                _sharedBitmap = new WriteableBitmap(
                    width, height, 96, 96, PixelFormats.Bgra32, null);
            }

            _sharedBitmap.Lock();
            _sharedBitmap.WritePixels(
                new System.Windows.Int32Rect(0, 0, width, height), pixels, stride, 0);
            _sharedBitmap.Unlock();

            lock (_liveFrameLock)
            {
                _latestFrame = _sharedBitmap;
                _lastFrameReceivedAtUtc = DateTime.UtcNow;
            }

            Interlocked.Exchange(ref _lastDispatchedTicksUtc, nowTicks);
            FrameAvailable?.Invoke(_sharedBitmap);
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>
    /// Measures and logs the actual dispatched FPS once per second.
    /// </summary>
    private void MeasureAndLogActualFps(long nowTicks)
    {
        Interlocked.Increment(ref _actualFpsCounter);
        var windowStart = Interlocked.Read(ref _fpsWindowStartTicks);
        if (nowTicks - windowStart < 10_000_000) return;

        var count = Interlocked.Exchange(ref _actualFpsCounter, 0);
        Interlocked.Exchange(ref _fpsWindowStartTicks, nowTicks);
        _ = _logger.InfoAsync($"[Canon] Live actual FPS: {count}");
    }

    public void Stop()
    {
        _isStopping = true;
        _isCapturing = false;

        if (_liveCaptureCts is not null)
        {
            _liveCaptureCts.Cancel();
            _liveCaptureCts.Dispose();
            _liveCaptureCts = null;
        }

        if (_liveCaptureLoop is not null)
        {
            try { _liveCaptureLoop.Wait(TimeSpan.FromSeconds(3)); } catch { }
            _liveCaptureLoop = null;
        }

        if (_liveCapture is not null)
        {
            try { _liveCapture.Release(); } catch { }
            _liveCapture.Dispose();
            _liveCapture = null;
        }

        IsRunning = false;
        lock (_liveFrameLock) { _latestFrame = null; _lastFrameReceivedAtUtc = null; }
        _isStopping = false;
    }

    public void Dispose() => Stop();
}
