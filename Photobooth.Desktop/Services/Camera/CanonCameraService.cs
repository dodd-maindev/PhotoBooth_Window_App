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
    private readonly object _frameLock = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _captureCts;
    private Task? _captureLoop;
    private volatile bool _isCapturing;
    private volatile bool _isStopping;
    private BitmapSource? _latestFrame;
    private DateTime? _lastFrameReceivedAtUtc;

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
        get { lock (_frameLock) { return _lastFrameReceivedAtUtc; } }
    }

    public bool TryGetLatestFrame(out ImageSource? frame)
    {
        lock (_frameLock) { frame = _latestFrame; return frame is not null; }
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
        await _logger.InfoAsync("[Canon] Trying device index: " + deviceIndex + " (preferred: '" + _preferredCameraName + "')").ConfigureAwait(false);

        try
        {
            _capture = new VideoCapture(deviceIndex);

            if (!_capture.IsOpened())
            {
                await _logger.WarnAsync("[Canon] Cannot open device index " + deviceIndex + ". Trying fallback devices...").ConfigureAwait(false);
                _capture.Dispose();
                _capture = null;

                for (var i = 0; i < 10; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return false;
                    var fallback = new VideoCapture(i);
                    if (fallback.IsOpened())
                    {
                        _capture = fallback;
                        deviceIndex = i;
                        await _logger.InfoAsync("[Canon] Opened fallback device index: " + i).ConfigureAwait(false);
                        break;
                    }
                    fallback.Dispose();
                }

                if (_capture is null || !_capture.IsOpened())
                {
                    LastStatusMessage = "Khong mo duoc camera video nao.";
                    await _logger.WarnAsync("[Canon] " + LastStatusMessage).ConfigureAwait(false);
                    return false;
                }
            }

            _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
            _capture.Set(VideoCaptureProperties.FrameHeight, 1080);
            _capture.Set(VideoCaptureProperties.Fps, 30);

            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isCapturing = true;
            _captureLoop = Task.Run(() => CaptureLoop(_captureCts.Token), _captureCts.Token);

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
        if (!string.IsNullOrWhiteSpace(_preferredCameraName))
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    using var cap = new VideoCapture(i);
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
        if (_deviceIndex >= 0) return _deviceIndex;
        return 0;
    }

    private void CaptureLoop(CancellationToken ct)
    {
        using var frame = new Mat();

        while (!ct.IsCancellationRequested && _isCapturing)
        {
            try
            {
                if (_capture is null || !_capture.IsOpened())
                    break;

                if (!_capture.Read(frame) || frame.Empty())
                {
                    if (ct.IsCancellationRequested) break;
                    Thread.Sleep(10);
                    continue;
                }

                if (_isStopping) break;

                using var rgbFrame = new Mat();
                Cv2.CvtColor(frame, rgbFrame, ColorConversionCodes.BGR2BGRA);

                var bitmapSource = MatToBitmapSource(rgbFrame);
                lock (_frameLock) { _latestFrame = bitmapSource; _lastFrameReceivedAtUtc = DateTime.UtcNow; }
                FrameAvailable?.Invoke(bitmapSource);
            }
            catch (AccessViolationException ex)
            {
                _logger.ErrorAsync("[Canon] AccessViolation in capture loop: " + ex.Message, ex).Wait();
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.ErrorAsync("[Canon] Frame capture error: " + ex.Message, ex).Wait();
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

    private void ClearLatestFrame()
    {
        lock (_frameLock) { _latestFrame = null; _lastFrameReceivedAtUtc = null; }
    }

    public void Dispose() => Stop();
}
