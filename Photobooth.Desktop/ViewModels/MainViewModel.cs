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
    /// <summary>Raised when the operator requests to end the current session.</summary>
    public event EventHandler? EndSessionRequested;
    private readonly AppSettings _settings;
    private readonly FileLogger _logger;
    private readonly ICameraService _cameraService;
    private readonly ImageProcessingClient _imageClient;
    private readonly CameraFolderWatcher _watcher;
    private readonly PrintService _printService;
    private readonly SessionFolderService _sessionFolderService;
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
    private string _customerName = string.Empty;

    /// <summary>Stores the UI mode setting for the window to read.</summary>
    public UiMode UiModeSetting { get; private set; }

    /// <summary>Provides access to localization for data binding.</summary>
    public LocalizationService Loc => LocalizationService.Instance;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        UiModeSetting = _settings.UiMode;

        // Initialize localization service with saved language
        LocalizationService.Instance.CurrentLanguage = _settings.Language;

        _sessionFolderService = new SessionFolderService();
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

        // Icon data: WPF geometry mini-language derived from Lucide icon SVGs (viewBox 0 0 24 24).
        Filters = new ObservableCollection<FilterOption>
        {
            new("grayscale", "Grayscale", "Ảnh đen trắng rõ nét",
                "M2.062 12.348 A1 1 0 0 1 2.062 11.652 A10.75 10.75 0 0 1 21.938 11.652 A1 1 0 0 1 21.938 12.348 A10.75 10.75 0 0 1 2.062 12.348 M9 12 A3 3 0 1 0 15 12 A3 3 0 1 0 9 12"),
            new("blur", "Blur", "Làm mờ nhẹ để tạo hiệu ứng",
                "M2 12 A10 10 0 1 0 22 12 A10 10 0 1 0 2 12 M14.31 8 L20.05 17.94 M9.69 8 L21.17 8 M7.38 12 L13.12 2.06 M9.69 16 L3.95 6.06 M14.31 16 L2.83 16 M20.1 12 L14.36 21.94"),
            new("vintage", "Vintage", "Tông cổ điển, ấm và mềm",
                "M5 3 H19 A2 2 0 0 1 21 5 V19 A2 2 0 0 1 19 21 H5 A2 2 0 0 1 3 5 V19 A2 2 0 0 1 5 21 M7 3 V21 M3 7.5 H7 M3 12 H21 M3 16.5 H7 M17 3 V21 M17 7.5 H21 M17 16.5 H21"),
            new("beauty", "Beauty", "Làm mịn da cơ bản",
                "M2 12 A10 10 0 1 0 22 12 A10 10 0 1 0 2 12 M8 14 C8 14 9.5 16 12 16 C14.5 16 16 14 16 14 M9 9 L9.01 9 M15 9 L15.01 9"),
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
        EndSessionCommand = new RelayCommand(_ => {
            _sessionFolderService.EndSession();
            _logger.InfoAsync("Session ended.").ConfigureAwait(false);
            EndSessionRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    public ObservableCollection<FilterOption> Filters { get; }

    public ICommand SelectFilterCommand { get; }

    public ICommand PrintSingleCommand { get; }

    public ICommand PrintFourCommand { get; }

    public ICommand CaptureCommand { get; }

    public ICommand ClearHistoryCommand { get; }

    /// <summary>Closes the current session and returns to the customer input screen.</summary>
    public ICommand EndSessionCommand { get; }

    /// <summary>Gets or sets the name of the current customer displayed in the header.</summary>
    public string CustomerName
    {
        get => _customerName;
        set => SetProperty(ref _customerName, value);
    }

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

    public SessionFolderService SessionFolderService => _sessionFolderService;

    /// <summary>Gets the current UI mode (Landscape or Portrait) from settings.</summary>
    public UiMode CurrentUiMode => _settings.UiMode;

    public void StartSession(string customerName)
    {
        _sessionFolderService.StartNewSession(customerName);
        CustomerName = customerName;
        _logger.InfoAsync($"Session started: {customerName}").ConfigureAwait(false);
    }

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
        UpdateStatus("Sẵn sàng.");
    }

    private async Task StartCameraPreviewAsync()
    {

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

        // Lưu vào session folder
        var sessionOriginalPath = _sessionFolderService.SaveOriginalPhoto(args.CopiedPath);
        if (!string.IsNullOrEmpty(sessionOriginalPath))
        {
            _logger.InfoAsync($"Original photo saved to session: {sessionOriginalPath}").ConfigureAwait(false);
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StatusMessage = $"Đã nhận ảnh mới: {Path.GetFileName(args.CopiedPath)}";
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

            // Lưu ảnh đã filter vào session folder
            if (SelectedFilter != null && !string.IsNullOrEmpty(_lastCapturedImagePath))
            {
                _sessionFolderService.SetCurrentFilter(SelectedFilter.Key);
                var sessionFilteredPath = _sessionFolderService.SaveFilteredPhoto(outputPath, SelectedFilter.Key);
                if (!string.IsNullOrEmpty(sessionFilteredPath))
                {
                    _logger.InfoAsync($"Filtered photo saved to session: {sessionFilteredPath}").ConfigureAwait(false);
                }
            }

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
            await _logger.WarnAsync($"Capture command blocked. CameraRunning={_cameraService.IsRunning}, HasFrame={_cameraService.TryGetLatestFrame(out _)}, LastFrameUtc={_cameraService.LastFrameReceivedAtUtc:O}").ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                LastErrorMessage = "Camera chưa sẵn sàng để chụp.";
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

            // Lưu vào session folder
            var sessionOriginalPath = _sessionFolderService.SaveOriginalPhoto(capturedPath);
            if (!string.IsNullOrEmpty(sessionOriginalPath))
            {
                _logger.InfoAsync($"Original photo saved to session: {sessionOriginalPath}").ConfigureAwait(false);
            }

            // Lưu filtered photo nếu có filter
            if (SelectedFilter != null)
            {
                _sessionFolderService.SetCurrentFilter(SelectedFilter.Key);
            }

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