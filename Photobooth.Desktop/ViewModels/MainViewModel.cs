using System.IO;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Photobooth.Desktop.Models;
using Photobooth.Desktop.Services;
using Photobooth.Desktop.Services.Camera;

namespace Photobooth.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly FileLogger _logger;
    private readonly ICameraService _cameraService;
    private readonly ImageProcessingClient _imageClient;
    private readonly CameraFolderWatcher _watcher;
    private readonly PrintService _printService;
    private readonly Channel<CameraPhotoReadyEventArgs> _processingQueue = Channel.CreateUnbounded<CameraPhotoReadyEventArgs>();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _queueWorker;
    private readonly HashSet<string> _recentProcessedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private bool _isInitialized;

    private ImageSource? _currentCameraImage;
    private ImageSource? _currentCapturedImage;
    private ImageSource? _currentProcessedImage;
    private string? _lastCapturedImagePath;
    private string _statusMessage = "Sẵn sàng.";
    private string _lastErrorMessage = string.Empty;
    private string _cameraStatusMessage = "Đang chờ camera...";
    private bool _isBusy;
    private FilterOption? _selectedFilter;

    public MainViewModel()
    {
        _settings = AppSettings.Load(AppContext.BaseDirectory);
        EnsureFolders();

        _logger = new FileLogger(_settings.LogFolder);
        
        if (_settings.CameraType == CameraType.Fuji)
        {
            _cameraService = new FujiCameraService(_logger, _settings.FujiSaveFolder, _settings.FujiPreferredCameraName, _settings.FujiWebcamDeviceIndex);
        }
        else
        {
            _cameraService = new CanonCameraService(_logger, _settings.CanonPreferredCameraName, _settings.CanonCameraDeviceIndex);
        }
        
        _cameraService.FrameAvailable += HandleCameraFrame;
        _cameraService.PhotoCaptured += async path => await HandlePhotoReadyAsync(new CameraPhotoReadyEventArgs(path, path));
        
        _imageClient = new ImageProcessingClient(_settings, _logger);
        _printService = new PrintService();
        _watcher = new CameraFolderWatcher(_settings.WatchFolder, _settings.ProcessingFolder, _logger);
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
                var isSame = filter == _selectedFilter;
                SelectedFilter = filter;
                
                // If user selects the same filter again, still re-process
                if (isSame && !string.IsNullOrEmpty(_lastCapturedImagePath) && File.Exists(_lastCapturedImagePath))
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
    }

    public ObservableCollection<FilterOption> Filters { get; }

    public ICommand SelectFilterCommand { get; }

    public ICommand PrintSingleCommand { get; }

    public ICommand PrintFourCommand { get; }

    public ICommand CaptureCommand { get; }

    public ICommand ClearHistoryCommand { get; }

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

    public FilterOption? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                UpdateStatus($"Filter đang chọn: {value?.DisplayName}");
                _ = _logger.InfoAsync($"Filter changed to: {value?.Key}");
                
                // Automatically re-process the last captured image with the new filter
                if (!string.IsNullOrEmpty(_lastCapturedImagePath))
                {
                    var exists = File.Exists(_lastCapturedImagePath);
                    _ = _logger.InfoAsync($"Last captured image: {_lastCapturedImagePath} (exists={exists})");
                    
                    if (exists)
                    {
                        _ = _logger.InfoAsync($"Triggering ReprocessImageWithFilterAsync for: {_lastCapturedImagePath}");
                        _ = ReprocessImageWithFilterAsync();
                    }
                    else
                    {
                        _ = _logger.WarnAsync($"Last captured image file not found: {_lastCapturedImagePath}");
                    }
                }
                else
                {
                    _ = _logger.WarnAsync("No last captured image path available for re-processing");
                }
            }
            else
            {
                _ = _logger.DebugAsync($"SelectedFilter set to same value: {value?.Key}, skipping re-process");
            }
        }
    }

    private async Task ReprocessImageWithFilterAsync()
    {
        var imagePath = _lastCapturedImagePath ?? "";
        if (!string.IsNullOrEmpty(imagePath))
        {
            // Queue it to avoid deadlocks with _processingLock
            await _processingQueue.Writer.WriteAsync(new CameraPhotoReadyEventArgs(imagePath, imagePath), _cts.Token).ConfigureAwait(false);
        }
    }

    public string SettingsSummary => $"Watch: {_settings.WatchFolder} | API: {_settings.ApiBaseUrl}";

    public ObservableCollection<string> RecentProcessedImages { get; } = new();

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await StartCameraPreviewAsync().ConfigureAwait(false);
        _watcher.Start();
        await _logger.InfoAsync("Photobooth desktop started.").ConfigureAwait(false);
        UpdateStatus("Đang theo dõi thư mục camera và live camera feed...");
    }

    private async Task StartCameraPreviewAsync()
    {
        if (_settings.CameraType == CameraType.Canon)
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
        }

        var started = await _cameraService.StartAsync(_cts.Token).ConfigureAwait(false);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CameraStatusMessage = _cameraService.LastStatusMessage;
            StatusMessage = started ? "Camera live view đã sẵn sàng." : "Không mở được camera live view.";
            RefreshCaptureCommandState();
        });

        if (!started)
        {
            await _logger.WarnAsync(_cameraService.LastStatusMessage).ConfigureAwait(false);
        }
    }

    private void HandleCameraFrame(ImageSource frame)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CurrentCameraImage = frame;
            CameraStatusMessage = _cameraService.LastStatusMessage;
            RefreshCaptureCommandState();
        });
    }

    private async Task HandlePhotoReadyAsync(CameraPhotoReadyEventArgs args)
    {
        _lastCapturedImagePath = args.CopiedPath;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateStatus($"Đã nhận ảnh mới: {Path.GetFileName(args.CopiedPath)}");
            CurrentCapturedImage = ImageSourceFactory.LoadFromFile(args.CopiedPath);
            CurrentProcessedImage = CurrentCapturedImage;
            AddRecentProcessedImage(args.CopiedPath);
        });
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

            var outputPath = await _imageClient.ProcessAsync(photo.CopiedPath, filter, _settings.OutputFolder, _cts.Token).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CurrentProcessedImage = ImageSourceFactory.LoadFromFile(outputPath);
                StatusMessage = $"Xử lý xong: {Path.GetFileName(outputPath)}";
                AddRecentProcessedImage(outputPath);
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
            var isEosTether = _settings.CameraType == CameraType.Canon && CanonUtilityDetector.DetectMode() == CanonConnectionMode.EosUtilityTether;
            await _logger.WarnAsync($"Capture command blocked. CameraRunning={_cameraService.IsRunning}, HasFrame={_cameraService.TryGetLatestFrame(out _)}, LastFrameUtc={_cameraService.LastFrameReceivedAtUtc:O}").ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LastErrorMessage = isEosTether
                    ? "Đang ở EOS Utility tether mode. Hãy bấm nút chụp trên máy ảnh để ảnh được gửi vào thư mục watch."
                    : "Camera chưa sẵn sàng để chụp.";
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

        await _logger.InfoAsync($"Capture command started. CameraRunning={_cameraService.IsRunning}, HasFrame={_cameraService.TryGetLatestFrame(out _)}, LastFrameUtc={_cameraService.LastFrameReceivedAtUtc:O}").ConfigureAwait(false);

        try
        {
            var capturedPath = await _cameraService.CaptureLatestFrameAsync(_settings.ProcessingFolder, _cts.Token).ConfigureAwait(false);
            _lastCapturedImagePath = capturedPath;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CurrentCapturedImage = ImageSourceFactory.LoadFromFile(capturedPath);
                CurrentProcessedImage = CurrentCapturedImage;
                StatusMessage = $"Đã chụp và lưu ảnh: {Path.GetFileName(capturedPath)}";
                AddRecentProcessedImage(capturedPath);
            });

            await _logger.InfoAsync($"Capture command finished. Saved={capturedPath}").ConfigureAwait(false);
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
        return _cameraService.IsRunning && _cameraService.TryGetLatestFrame(out _);
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

    public void Dispose()
    {
        _cts.Cancel();
        _cameraService.Dispose();
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
        Directory.CreateDirectory(_settings.WatchFolder);
        Directory.CreateDirectory(_settings.ProcessingFolder);
        Directory.CreateDirectory(_settings.OutputFolder);
        Directory.CreateDirectory(_settings.LogFolder);
    }
}