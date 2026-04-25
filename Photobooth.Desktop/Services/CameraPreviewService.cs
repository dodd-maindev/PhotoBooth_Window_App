using OpenCvSharp;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Desktop.Services;

public sealed class CameraPreviewService : IDisposable
{
    private readonly FileLogger _logger;
    private readonly object _frameLock = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _captureCts;
    private Task? _captureLoop;
    private volatile bool _isCapturing;
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

        ClearLatestFrame();

        var resolvedIndex = ResolveDeviceIndex(preferredCameraName, deviceIndex);
        await _logger.InfoAsync($"[CameraPreview] Starting webcam preview on device index: {resolvedIndex}").ConfigureAwait(false);

        try
        {
            _capture = new VideoCapture(resolvedIndex, VideoCaptureAPIs.DSHOW);

            if (!_capture.IsOpened())
            {
                await _logger.WarnAsync($"[CameraPreview] Cannot open device index {resolvedIndex}. Trying fallback devices...").ConfigureAwait(false);
                _capture.Dispose();
                _capture = null;

                for (var i = 0; i < 10; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        var fallback = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                        if (fallback.IsOpened())
                        {
                            _capture = fallback;
                            resolvedIndex = i;
                            await _logger.InfoAsync($"[CameraPreview] Opened fallback device index: {i}").ConfigureAwait(false);
                            break;
                        }
                        fallback.Dispose();
                    }
                    catch { }
                }

                if (_capture is null || !_capture.IsOpened())
                {
                    LastStatusMessage = "Không tìm thấy camera video nào trên máy.";
                    await _logger.WarnAsync(LastStatusMessage).ConfigureAwait(false);
                    return false;
                }
            }

            _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
            _capture.Set(VideoCaptureProperties.FrameWidth, 1280);
            _capture.Set(VideoCaptureProperties.FrameHeight, 720);
            _capture.Set(VideoCaptureProperties.Fps, 30);
            _capture.Set(VideoCaptureProperties.BufferSize, 1);

            var actualWidth = _capture.Get(VideoCaptureProperties.FrameWidth);
            var actualHeight = _capture.Get(VideoCaptureProperties.FrameHeight);
            var actualFps = _capture.Get(VideoCaptureProperties.Fps);
            await _logger.InfoAsync($"[CameraPreview] Actual settings: {actualWidth}x{actualHeight} @ {actualFps} fps").ConfigureAwait(false);

            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isCapturing = true;
            _captureLoop = Task.Run(() => CaptureLoop(_captureCts.Token), _captureCts.Token);

            var started = await WaitForStartAsync(cancellationToken).ConfigureAwait(false);
            if (!started)
            {
                Stop();
                LastStatusMessage = "Không mở được camera.";
                await _logger.WarnAsync(LastStatusMessage).ConfigureAwait(false);
                return false;
            }

            IsRunning = true;
            LastStatusMessage = "Camera live view đang chạy.";
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

    private static int ResolveDeviceIndex(string preferredCameraName, int deviceIndex)
    {
        if (!string.IsNullOrWhiteSpace(preferredCameraName))
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                    if (cap.IsOpened())
                    {
                        var name = "device-" + i;
                        if (name.Contains(preferredCameraName, StringComparison.OrdinalIgnoreCase))
                            return i;
                    }
                }
                catch { }
            }
        }

        if (deviceIndex >= 0) return deviceIndex;
        return 0;
    }

    private async Task<bool> WaitForStartAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 50; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_capture is not null && _capture.IsOpened())
            {
                return true;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private void CaptureLoop(CancellationToken ct)
    {
        using var frame = new Mat();
        using var rgbFrame = new Mat();

        while (!ct.IsCancellationRequested && _isCapturing)
        {
            try
            {
                if (_capture is null || !_capture.IsOpened())
                    break;

                if (!_capture.Read(frame) || frame.Empty())
                {
                    if (ct.IsCancellationRequested) break;
                    Thread.Sleep(5);
                    continue;
                }

                if (_isStopping) break;

                Cv2.CvtColor(frame, rgbFrame, ColorConversionCodes.BGR2BGRA);
                var bitmapSource = MatToBitmapSource(rgbFrame);

                lock (_frameLock)
                {
                    _latestFrame = bitmapSource;
                    _lastFrameReceivedAtUtc = DateTime.UtcNow;
                }

                FrameAvailable?.Invoke(bitmapSource);
            }
            catch (AccessViolationException ex)
            {
                _logger.ErrorAsync($"[CameraPreview] AccessViolation in capture loop: {ex.Message}", ex).Wait();
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.ErrorAsync($"[CameraPreview] Frame capture error: {ex.Message}", ex).Wait();
            }
        }
    }

    private static BitmapSource MatToBitmapSource(Mat mat)
    {
        var width = mat.Width;
        var height = mat.Height;
        var stride = width * 4;
        var pixels = new byte[height * stride];

        Marshal.Copy(mat.Data, pixels, 0, pixels.Length);

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, stride, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private void ClearLatestFrame()
    {
        lock (_frameLock)
        {
            _latestFrame = null;
            _lastFrameReceivedAtUtc = null;
        }
    }

    public void Stop()
    {
        _isStopping = true;
        _isCapturing = false;

        if (_captureCts is not null)
        {
            _captureCts.Cancel();
            _captureCts.Dispose();
            _captureCts = null;
        }

        if (_captureLoop is not null)
        {
            try { _captureLoop.Wait(TimeSpan.FromSeconds(3)); } catch { }
            _captureLoop = null;
        }

        if (_capture is not null)
        {
            try { _capture.Release(); } catch { }
            _capture.Dispose();
            _capture = null;
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