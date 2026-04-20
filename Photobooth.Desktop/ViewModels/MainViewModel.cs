using System.IO;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Photobooth.Desktop.Models;
using Photobooth.Desktop.Services;

namespace Photobooth.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly FileLogger _logger;
    private readonly CameraPreviewService _cameraPreview;
    private readonly ImageProcessingClient _imageClient;
    private readonly CameraFolderWatcher _watcher;
    private readonly PrintService _printService;
    private readonly Channel<CameraPhotoReadyEventArgs> _processingQueue = Channel.CreateUnbounded<CameraPhotoReadyEventArgs>();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _queueWorker;
    private readonly HashSet<string> _recentProcessedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly object _filterLock = new();
    private readonly string _watchFolder;
    private readonly string _processingFolder;
    private readonly string _outputFolder;
    private bool _isInitialized;

    private ImageSource? _currentCameraImage;
    private ImageSource? _currentCapturedImage;
    private ImageSource? _currentProcessedImage;
    private string? _lastCapturedImagePath;
    private string _statusMessage = "Sẵn sàng.";
    private string _lastErrorMessage = string.Empty;
    private string _cameraStatusMessage = "Đang chờ camera...";
    private bool _isBusy;
    private bool _isFilterChanging;
    private FilterOption? _selectedFilter;

    public string CustomerName { get; }

    public MainViewModel(AppSettings settings, CustomerSessionService sessionService, string customerName, string watchFolder, string processingFolder, string outputFolder)
    {
        _settings = settings;
        CustomerName = customerName;
        _watchFolder = watchFolder;
        _processingFolder = processingFolder;
        _outputFolder = outputFolder;

        EnsureFolders();

        _logger = new FileLogger(_settings.LogFolder);
        _cameraPreview = new CameraPreviewService(_logger);
        _cameraPreview.FrameAvailable += HandleCameraFrame;
        _imageClient = new ImageProcessingClient(_settings, _logger);
        _printService = new PrintService();
        _watcher = new CameraFolderWatcher(_watchFolder, _processingFolder, _logger);
        _watcher.PhotoReady += HandlePhotoReadyAsync;
        _queueWorker = Task.Run(ProcessQueueAsync);

        Filters = new ObservableCollection<FilterOption>
        {
            new("grayscale", "Grayscale", "Ảnh đen trắng rõ nét"),
            new("blur", "Blur", "Làm mờ nhẹ để tạo hiệu ứng"),
            new("vintage", "Vintage", "Tông cổ điển, ấm và mềm"),
            new("beauty", "Beauty", "Làm mịn da cơ bản"),
            new("remove_background", "Remove background", "Tách nền bằng AI nếu cài mediapipe")
        };

        _selectedFilter = Filters.FirstOrDefault(x => x.Key == "beauty") ?? Filters.FirstOrDefault();

        SelectFilterCommand = new RelayCommand(parameter =>
        {
            if (parameter is FilterOption filter)
            {
                _ = _logger.InfoAsync($"SelectFilterCommand triggered with filter: {filter.Key}");
                SelectedFilter = filter;

                if (filter == _selectedFilter && !string.IsNullOrEmpty(_lastCapturedImagePath) && File.Exists(_lastCapturedImagePath))
                {
                    _ = _logger.InfoAsync($"Same filter selected again, re-processing: {_lastCapturedImagePath}");
                    _ = ReprocessImageWithFilterAsync();
                }
            }
        });

        PrintSingleCommand = new RelayCommand(_ => PrintSingle());
        PrintFourCommand = new RelayCommand(_ => PrintFour());
        CaptureCommand = new AsyncRelayCommand(async _ => await CaptureCurrentFrameAsync().ConfigureAwait(false));
        ClearHistoryCommand = new RelayCommand(_ => ClearHistory());
        EndSessionCommand = new RelayCommand(_ => EndSession());
    }

    public ObservableCollection<FilterOption> Filters { get; }

    public ICommand SelectFilterCommand { get; }

    public ICommand PrintSingleCommand { get; }

    public ICommand PrintFourCommand { get; }

    public ICommand CaptureCommand { get; }

    public ICommand ClearHistoryCommand { get; }

    public ICommand EndSessionCommand { get; }

    public ImageSource? CurrentCameraImage
    {
        get => _currentCameraImage;
        private set => SetProperty(ref _currentCameraImage, value);
    }

    public ImageSource? CurrentProcessedImage
    {
        get => _currentProcessedImage;
        private set => SetProperty(ref _currentProcessedImage, value);
    }

    public ImageSource? CurrentCapturedImage
    {
        get => _currentCapturedImage;
        private set => SetProperty(ref _currentCapturedImage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LastErrorMessage
    {
        get => _lastErrorMessage;
        private set => SetProperty(ref _lastErrorMessage, value);
    }

    public string CameraStatusMessage
    {
        get => _cameraStatusMessage;
        private set => SetProperty(ref _cameraStatusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsFilterChanging
    {
        get => _isFilterChanging;
        private set => SetProperty(ref _isFilterChanging, value);
    }

    public FilterOption? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                UpdateStatus($"Filter đang chọn: {value?.DisplayName}");
                _ = _logger.InfoAsync($"Filter changed to: {value?.Key}");

                if (!string.IsNullOrEmpty(_lastCapturedImagePath) && File.Exists(_lastCapturedImagePath))
                {
                    _ = ReprocessImageWithFilterAsync();
                }
            }
        }
    }

    private async Task ReprocessImageWithFilterAsync()
    {
        string? imagePath;
        string filter;

        lock (_filterLock)
        {
            imagePath = _lastCapturedImagePath;
            filter = _selectedFilter?.Key ?? "beauty";
        }

        if (string.IsNullOrEmpty(imagePath))
        {
            return;
        }

        IsFilterChanging = true;
        StatusMessage = $"Đang áp dụng filter '{filter}'...";

        await _processingLock.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            var outputPath = await _imageClient.ProcessAsync(imagePath, filter, _outputFolder, _cts.Token).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    CurrentProcessedImage = ImageSourceFactory.LoadFromFile(outputPath);
                    StatusMessage = $"Đã áp dụng filter: {filter}";
                }
                catch (Exception ex)
                {
                    LastErrorMessage = ex.Message;
                    StatusMessage = "Áp dụng filter thất bại.";
                }
            });
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Filter reprocessing failed for {imagePath}", ex).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LastErrorMessage = ex.Message;
                StatusMessage = "Áp dụng filter thất bại.";
            });
        }
        finally
        {
            _processingLock.Release();
            await Application.Current.Dispatcher.InvokeAsync(() => IsFilterChanging = false);
        }
    }

    public string SettingsSummary => $"Khách: {CustomerName}";

    public ObservableCollection<string> RecentProcessedImages { get; } = new();

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        try
        {
            await StartCameraPreviewAsync().ConfigureAwait(false);
            _watcher.Start();
            await _logger.InfoAsync("Photobooth desktop started.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("InitializeAsync failed", ex).ConfigureAwait(false);
        }
    }

    private async Task StartCameraPreviewAsync()
    {
        try
        {
            var mode = CanonUtilityDetector.DetectMode();
            await _logger.InfoAsync($"Detected Canon connection mode: {mode}").ConfigureAwait(false);

            if (mode == CanonConnectionMode.EosUtilityTether)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CameraStatusMessage = "Đang dùng Canon EOS Utility (tether). Hệ thống sẽ nhận ảnh từ thư mục watch khi bấm chụp trên máy ảnh.";
                    StatusMessage = "Tether mode đã bật. Nút Capture trong app sẽ không dùng trong mode này.";
                    RefreshCaptureCommandState();
                });

                return;
            }

            var started = await _cameraPreview.StartAsync(_settings.PreferredCameraName, _settings.CameraDeviceIndex, _cts.Token).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CameraStatusMessage = started
                    ? _cameraPreview.LastStatusMessage
                    : mode == CanonConnectionMode.None
                        ? "Không phát hiện EOS Webcam Utility hoặc EOS Utility."
                        : _cameraPreview.LastStatusMessage;
                StatusMessage = started ? "Camera live view đã sẵn sàng." : $"Không mở được camera live view cho '{_settings.PreferredCameraName}'.";
                RefreshCaptureCommandState();
            });

            if (!started)
            {
                await _logger.WarnAsync(_cameraPreview.LastStatusMessage).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("StartCameraPreviewAsync failed", ex).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CameraStatusMessage = "Lỗi khởi tạo camera: " + ex.Message;
                StatusMessage = "Camera không khả dụng. Vẫn có thể nhận ảnh từ thư mục watch.";
            });
        }
    }

    private void HandleCameraFrame(ImageSource frame)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CurrentCameraImage = frame;
            CameraStatusMessage = _cameraPreview.LastStatusMessage;
            RefreshCaptureCommandState();
        });
    }

    private async Task HandlePhotoReadyAsync(CameraPhotoReadyEventArgs args)
    {
        _lastCapturedImagePath = args.CopiedPath;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateStatus($"Đã nhận ảnh mới: {Path.GetFileName(args.CopiedPath)}");
        });

        await _processingQueue.Writer.WriteAsync(args, _cts.Token).ConfigureAwait(false);
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var item in _processingQueue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                await ProcessSingleImageAsync(item).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessSingleImageAsync(CameraPhotoReadyEventArgs photo)
    {
        var filter = SelectedFilter?.Key ?? "beauty";

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsBusy = true;
            LastErrorMessage = string.Empty;
            StatusMessage = $"Đang tải ảnh lên Python service: {Path.GetFileName(photo.CopiedPath)}";
        });

        await _processingLock.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusMessage = $"Đang xử lý filter '{filter}'...";
            });

            var outputPath = await _imageClient.ProcessAsync(photo.CopiedPath, filter, _outputFolder, _cts.Token).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    CurrentProcessedImage = ImageSourceFactory.LoadFromFile(outputPath);
                    StatusMessage = $"Xử lý xong: {Path.GetFileName(outputPath)}";
                    AddRecentProcessedImage(outputPath);
                }
                catch (Exception ex)
                {
                    LastErrorMessage = ex.Message;
                    StatusMessage = "Tải ảnh thất bại.";
                }
            });

            if (_settings.EnableAutoPrint)
            {
                PrintSingle(outputPath);
            }
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Processing failed for {photo.CopiedPath}", ex).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LastErrorMessage = ex.Message;
                StatusMessage = "Xử lý ảnh thất bại. Hệ thống vẫn tiếp tục lắng nghe.";
            });
        }
        finally
        {
            _processingLock.Release();
            await Application.Current.Dispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task CaptureCurrentFrameAsync()
    {
        if (!CanCapture())
        {
            var mode = CanonUtilityDetector.DetectMode();
            await _logger.WarnAsync($"Capture command blocked. CameraRunning={_cameraPreview.IsRunning}, HasFrame={_cameraPreview.TryGetLatestFrame(out _)}, LastFrameUtc={_cameraPreview.LastFrameReceivedAtUtc:O}").ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LastErrorMessage = mode == CanonConnectionMode.EosUtilityTether
                    ? "Đang ở EOS Utility tether mode. Hãy bấm nút chụp trên máy ảnh để ảnh được gửi vào thư mục watch."
                    : "Camera chưa sẵn sàng để chụp. Hãy kiểm tra lại EOS Webcam Utility.";
                StatusMessage = "Không thể chụp lúc này.";
            });

            return;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsBusy = true;
            LastErrorMessage = string.Empty;
            StatusMessage = "Đang chụp ảnh từ camera...";
        });

        await _logger.InfoAsync($"Capture command started. CameraRunning={_cameraPreview.IsRunning}, HasFrame={_cameraPreview.TryGetLatestFrame(out _)}, LastFrameUtc={_cameraPreview.LastFrameReceivedAtUtc:O}").ConfigureAwait(false);

        try
        {
            var capturedPath = await _cameraPreview.CaptureLatestFrameAsync(_processingFolder, _cts.Token).ConfigureAwait(false);
            _lastCapturedImagePath = capturedPath;

            ImageSource? capturedImage = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    capturedImage = ImageSourceFactory.LoadFromFile(capturedPath);
                    CurrentCapturedImage = capturedImage;
                    CurrentProcessedImage = capturedImage;
                    StatusMessage = $"Đã chụp và lưu ảnh: {Path.GetFileName(capturedPath)}";
                }
                catch (Exception ex)
                {
                    LastErrorMessage = "Lỗi tải ảnh: " + ex.Message;
                    StatusMessage = "Chụp ảnh thành công nhưng không hiển thị được ảnh.";
                }
            });

            await _logger.InfoAsync($"Capture command finished. Saved={capturedPath}").ConfigureAwait(false);

            await _processingQueue.Writer.WriteAsync(new CameraPhotoReadyEventArgs(capturedPath, capturedPath), _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("Capture failed", ex).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LastErrorMessage = ex.Message;
                StatusMessage = "Chụp ảnh thất bại.";
            });
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private bool CanCapture()
    {
        return _cameraPreview.IsRunning && _cameraPreview.TryGetLatestFrame(out _);
    }

    private void RefreshCaptureCommandState()
    {
        if (CaptureCommand is AsyncRelayCommand asyncCommand)
        {
            asyncCommand.RaiseCanExecuteChanged();
        }
    }

    private void AddRecentProcessedImage(string path)
    {
        if (_recentProcessedPaths.Add(path))
        {
            RecentProcessedImages.Insert(0, path);

            while (RecentProcessedImages.Count > 4)
            {
                var removed = RecentProcessedImages[^1];
                _recentProcessedPaths.Remove(removed);
                RecentProcessedImages.RemoveAt(RecentProcessedImages.Count - 1);
            }
        }
    }

    private void PrintSingle(string? imagePath = null)
    {
        var path = imagePath ?? RecentProcessedImages.FirstOrDefault();
        if (path is null)
        {
            UpdateStatus("Chưa có ảnh để in.");
            return;
        }

        try
        {
            if (_printService.PrintSingle(path))
            {
                UpdateStatus($"Đã gửi in ảnh: {Path.GetFileName(path)}");
            }
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            UpdateStatus("In ảnh thất bại.");
        }
    }

    private void PrintFour()
    {
        var images = RecentProcessedImages.Take(4).ToList();
        if (images.Count == 0)
        {
            UpdateStatus("Chưa có ảnh để in.");
            return;
        }

        try
        {
            if (_printService.PrintFour(images))
            {
                UpdateStatus("Đã gửi bộ in 4 ảnh.");
            }
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            UpdateStatus("In bộ 4 ảnh thất bại.");
        }
    }

    private void ClearHistory()
    {
        RecentProcessedImages.Clear();
        _recentProcessedPaths.Clear();
        UpdateStatus("Đã xóa lịch sử ảnh in.");
    }

    private void UpdateStatus(string message)
    {
        StatusMessage = message;
    }

    private void EndSession()
    {
        StatusMessage = "Đang kết thúc phiên...";
        _ = _logger.InfoAsync("EndSession requested by user.");
        EndSessionRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? EndSessionRequested;

    public void Dispose()
    {
        _cts.Cancel();
        _cameraPreview.Dispose();
        _watcher.Dispose();
        _imageClient.Dispose();
        _processingLock.Dispose();
        _cts.Dispose();

        try
        {
            _queueWorker.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private void EnsureFolders()
    {
        Directory.CreateDirectory(_watchFolder);
        Directory.CreateDirectory(_processingFolder);
        Directory.CreateDirectory(_outputFolder);
        Directory.CreateDirectory(_settings.LogFolder);
    }
}