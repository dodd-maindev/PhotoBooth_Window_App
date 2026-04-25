using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using OpenCvSharp;

namespace Photobooth.Desktop.Services.Camera;

public sealed class FujiCameraService : ICameraService
{
    private readonly FileLogger _logger;
    private readonly string _fujiSaveFolder;
    private readonly string _preferredCameraName;
    private readonly int _liveWebcamDeviceIndex;
    private readonly FileSystemWatcher _saveWatcher;
    private readonly Channel<string> _pendingPaths = Channel.CreateUnbounded<string>();
    private readonly ConcurrentDictionary<string, byte> _debounce = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _saveWatcherTask;
    private readonly object _liveFrameLock = new();
    private volatile bool _isStopping;

    private VideoCapture? _liveCapture;
    private CancellationTokenSource? _liveCaptureCts;
    private Task? _liveCaptureLoop;
    private volatile bool _isLiveCapturing;
    private WriteableBitmap? _sharedBitmap;
    private BitmapSource? _latestFrame;
    private DateTime? _lastFrameReceivedAtUtc;
    private long _lastDispatchedTicksUtc;
    private int _actualFpsCounter;
    private long _fpsWindowStartTicks;

    public FujiCameraService(FileLogger logger, string fujiSaveFolder, string preferredCameraName, int liveWebcamDeviceIndex)
    {
        _logger = logger;
        _fujiSaveFolder = fujiSaveFolder;
        _preferredCameraName = preferredCameraName;
        _liveWebcamDeviceIndex = liveWebcamDeviceIndex;

        Directory.CreateDirectory(_fujiSaveFolder);

        _saveWatcher = new FileSystemWatcher(_fujiSaveFolder)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = false,
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _saveWatcherTask = Task.Run(MonitorFujiSaveFolderAsync);
    }

    public event Action<ImageSource>? FrameAvailable;
    public event Func<string, Task>? PhotoCaptured;

    public bool IsRunning { get; private set; }
    public string LastStatusMessage { get; private set; } = "";

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
        await _logger.InfoAsync("[Fuji] Capture requested (tether mode via Tether App)").ConfigureAwait(false);
        throw new InvalidOperationException(
            "Fuji đang ở chế độ tether (Tether App). Hãy bấm nút chụp trên máy ảnh Fujifilm để ảnh được gửi vào thư mục.");
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) return true;
        await _logger.InfoAsync("[Fuji] Starting Fujifilm service...").ConfigureAwait(false);

        StartSaveFolderWatcher();

        var liveStarted = await StartLivePreviewAsync(cancellationToken).ConfigureAwait(false);

        IsRunning = true;
        if (liveStarted)
        {
            LastStatusMessage = "Fujifilm: Live preview running. Chờ chụp từ máy ảnh.";
        }
        else
        {
            LastStatusMessage = "Fujifilm Tether App: watching for photos. Live preview failed.";
        }
        await _logger.InfoAsync("[Fuji] " + LastStatusMessage).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> StartLivePreviewAsync(CancellationToken cancellationToken)
    {
        var deviceIndex = ResolveLiveWebcamDeviceIndex();
        await _logger.InfoAsync("[Fuji] Starting live preview on device index: " + deviceIndex + " (preferred: '" + _preferredCameraName + "')").ConfigureAwait(false);

        try
        {
            // Use DirectShow backend instead of MSMF for much better stability on Windows
            _liveCapture = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);

            if (!_liveCapture.IsOpened())
            {
                await _logger.WarnAsync("[Fuji] Cannot open live device index " + deviceIndex + " with DShow. Trying fallback devices...").ConfigureAwait(false);
                _liveCapture.Dispose();
                _liveCapture = null;

                for (var i = 0; i < 10; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return false;
                    try
                    {
                        var fallback = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                        if (fallback.IsOpened())
                        {
                            _liveCapture = fallback;
                            deviceIndex = i;
                            await _logger.InfoAsync("[Fuji] Opened live preview fallback device index: " + i).ConfigureAwait(false);
                            break;
                        }
                        fallback.Dispose();
                    }
                    catch { }
                }

                if (_liveCapture is null || !_liveCapture.IsOpened())
                {
                    await _logger.WarnAsync("[Fuji] No live webcam available for preview.").ConfigureAwait(false);
                    return false;
                }
            }

            // 640x480 @ 30fps matches this webcam's actual hardware delivery rate.
            _liveCapture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
            _liveCapture.Set(VideoCaptureProperties.FrameWidth, 640);
            _liveCapture.Set(VideoCaptureProperties.FrameHeight, 480);
            _liveCapture.Set(VideoCaptureProperties.Fps, 30);
            _liveCapture.Set(VideoCaptureProperties.BufferSize, 1);

            // Log what the driver actually negotiated (may differ from requested values).
            var actualWidth = _liveCapture.Get(VideoCaptureProperties.FrameWidth);
            var actualHeight = _liveCapture.Get(VideoCaptureProperties.FrameHeight);
            var actualFps = _liveCapture.Get(VideoCaptureProperties.Fps);
            await _logger.InfoAsync(
                $"[Fuji] Live webcam actual settings: {actualWidth}x{actualHeight} @ {actualFps} fps")
                .ConfigureAwait(false);

            _liveCaptureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isLiveCapturing = true;
            _liveCaptureLoop = Task.Run(() => LiveCaptureLoop(_liveCaptureCts.Token), _liveCaptureCts.Token);

            await _logger.InfoAsync("[Fuji] Live preview started on device " + deviceIndex).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("[Fuji] Failed to start live preview: " + ex.Message, ex).ConfigureAwait(false);
            StopLivePreview();
            return false;
        }
    }

    private int ResolveLiveWebcamDeviceIndex()
    {
        if (!string.IsNullOrWhiteSpace(_preferredCameraName))
        {
            for (var i = 0; i < 10; i++)
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

        if (_liveWebcamDeviceIndex >= 0) return _liveWebcamDeviceIndex;
        return 0;
    }

    private void LiveCaptureLoop(CancellationToken ct)
    {
        using var frame = new Mat();
        using var rgbFrame = new Mat();
        // Dispatch at 240 fps (≈4.17 ms).
        const long DispatchIntervalTicks = 41_667; // 4.17ms in 100ns ticks

        while (!ct.IsCancellationRequested && _isLiveCapturing)
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
                    continue; // Drop frame — save UI thread bandwidth

                Cv2.CvtColor(frame, rgbFrame, ColorConversionCodes.BGR2BGRA);
                DispatchFrame(rgbFrame, nowTicks);
                MeasureAndLogActualFps(nowTicks);
            }
            catch (AccessViolationException ex)
            {
                _logger.ErrorAsync("[Fuji] AccessViolation in live capture loop: " + ex.Message, ex).Wait();
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.ErrorAsync("[Fuji] Live frame capture error: " + ex.Message, ex).Wait();
            }
        }
    }

    /// <summary>
    /// Writes pixel data into the shared <see cref="WriteableBitmap"/> on the UI
    /// thread, avoiding per-frame heap allocations.
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
    /// Uses Interlocked for thread-safe access from the capture thread.
    /// </summary>
    private void MeasureAndLogActualFps(long nowTicks)
    {
        Interlocked.Increment(ref _actualFpsCounter);
        var windowStart = Interlocked.Read(ref _fpsWindowStartTicks);
        if (nowTicks - windowStart < 10_000_000) return; // less than 1 second elapsed

        var count = Interlocked.Exchange(ref _actualFpsCounter, 0);
        Interlocked.Exchange(ref _fpsWindowStartTicks, nowTicks);
        _ = _logger.InfoAsync($"[Fuji] Live actual FPS: {count}");
    }

    private void StartSaveFolderWatcher()
    {
        _saveWatcher.EnableRaisingEvents = true;
        _saveWatcher.Created += OnFujiSaveFileCreated;
        _saveWatcher.Changed += OnFujiSaveFileCreated;
        _saveWatcher.Renamed += OnFujiSaveFileRenamed;
        _ = _logger.InfoAsync("[Fuji] Watching folder for Tether App output: " + _fujiSaveFolder);
    }

    private void OnFujiSaveFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!IsImageFile(e.FullPath)) return;
        if (_debounce.TryAdd(e.FullPath, 0))
        {
            _ = _logger.InfoAsync("[Fuji] New file detected: " + e.FullPath);
            _pendingPaths.Writer.TryWrite(e.FullPath);
        }
    }

    private void OnFujiSaveFileRenamed(object sender, RenamedEventArgs e) => OnFujiSaveFileCreated(sender, e);

    private async Task MonitorFujiSaveFolderAsync()
    {
        try
        {
            await foreach (var path in _pendingPaths.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    var stablePath = await WaitForFileReadyAsync(path, _cts.Token).ConfigureAwait(false);
                    var imageSource = await LoadImageAsync(stablePath, _cts.Token).ConfigureAwait(false);

                    lock (_liveFrameLock) { _latestFrame = imageSource; _lastFrameReceivedAtUtc = DateTime.UtcNow; }
                    FrameAvailable?.Invoke(imageSource);

                    if (PhotoCaptured is not null)
                    {
                        await _logger.InfoAsync("[Fuji] Raising PhotoCaptured: " + stablePath).ConfigureAwait(false);
                        await PhotoCaptured.Invoke(stablePath).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    await _logger.ErrorAsync("[Fuji] Failed to process save folder file: " + path, ex).ConfigureAwait(false);
                }
                finally
                {
                    _debounce.TryRemove(path, out _);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Accepts only raster formats — skips RAW (.raf/.raw) for speed.</summary>
    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Waits until the file is exclusively available (X Acquire finished writing).</summary>
    private static async Task<string> WaitForFileReadyAsync(string path, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 200; i++) // 2 seconds max
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 0)
                {
                    // Attempt exclusive read handle. If Fuji is still writing, it throws IOException.
                    // Once it succeeds, we know the file is fully written and unlocked.
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return path;
                }
            }
            catch (IOException) { }
            await Task.Delay(10, cancellationToken).ConfigureAwait(false); // poll faster (10ms vs 50ms)
        }
        throw new IOException("Timeout waiting for file: " + path);
    }

    private static async Task<BitmapSource> LoadImageAsync(string path, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 1920; // Massive speedup: forces WPF to decode smaller resolution for preview
        bitmap.StreamSource = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void StopLivePreview()
    {
        _isLiveCapturing = false;

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
    }

    public void Stop()
    {
        _isStopping = true;
        _saveWatcher.EnableRaisingEvents = false;
        _saveWatcher.Created -= OnFujiSaveFileCreated;
        _saveWatcher.Changed -= OnFujiSaveFileCreated;
        _saveWatcher.Renamed -= OnFujiSaveFileRenamed;

        StopLivePreview();

        IsRunning = false;
        lock (_liveFrameLock) { _latestFrame = null; _lastFrameReceivedAtUtc = null; }
        _isStopping = false;
    }

    public void Dispose()
    {
        Stop();
        _cts.Cancel();
        _pendingPaths.Writer.TryComplete();
        try { _saveWatcherTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _saveWatcher.Dispose();
        _cts.Dispose();
    }
}
