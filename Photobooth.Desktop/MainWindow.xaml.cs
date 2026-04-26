using System;
using System.Windows;
using System.Windows.Input;
using Photobooth.Desktop.Models;
using Photobooth.Desktop.Services;
using Photobooth.Desktop.ViewModels;

namespace Photobooth.Desktop;

/// <summary>
/// Main photobooth session window. Hosts the live preview, filter selection,
/// and action toolbar. Delegates session lifecycle to <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Gets the ViewModel driving this window.</summary>
    public MainViewModel? ViewModel { get; }

    /// <summary>
    /// Initialises the window, wires up the ViewModel, and subscribes to the
    /// end-session event so the session loop in App.xaml.cs can react.
    /// </summary>
    public MainWindow(string customerName, AppSettings settings)
    {
        InitializeComponent();

        ViewModel = new MainViewModel();
        ViewModel.StartSession(customerName);
        ViewModel.EndSessionRequested += OnViewModelEndSessionRequested;

        DataContext = ViewModel;

        Loaded += MainWindow_Loaded;
        Closing += (_, _) =>
        {
            if (ViewModel is not null)
            {
                ViewModel.EndSessionRequested -= OnViewModelEndSessionRequested;
                ViewModel.Dispose();
            }
        };
    }

    /// <summary>Raised when the operator clicks "Kết thúc", triggering the session loop.</summary>
    public event EventHandler? EndSessionRequested;

    /// <summary>Forwards the ViewModel event to the window-level event for App.xaml.cs.</summary>
    private void OnViewModelEndSessionRequested(object? sender, EventArgs e)
    {
        EndSessionRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        try
        {
            // Đợi window render xong trước khi khởi tạo camera
            // DispatcherPriority.Render đảm bảo window đã hiển thị
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            // Bây giờ mới chạy InitializeAsync bất đồng bộ
            await ViewModel.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[MainWindow] InitializeAsync failed: {ex}");
            var logFolder = @"C:\photobooth\logs";
            System.IO.Directory.CreateDirectory(logFolder);
            System.IO.File.AppendAllText(System.IO.Path.Combine(logFolder, "boot.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] MainWindow InitializeAsync failed: {ex}{Environment.NewLine}");
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}