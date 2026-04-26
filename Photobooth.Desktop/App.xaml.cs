using System.IO;
using System.Windows;
using Photobooth.Desktop.Models;
using Photobooth.Desktop.Services;

namespace Photobooth.Desktop;

/// <summary>
/// Application entry point. Manages the WPF application lifecycle,
/// including starting and stopping the embedded Python service.
/// </summary>
public partial class App : Application
{
    private AppSettings? _settings;
    private PythonServiceHost? _pythonServiceHost;
    private LoadingDialog? _loadingDialog;

    /// <summary>
    /// Initialises global exception handlers, loads settings, starts the
    /// Python service, then enters the session loop.
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        TraceBoot("Application startup begin");

        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _settings = AppSettings.Load(AppContext.BaseDirectory);

            _pythonServiceHost = new PythonServiceHost();
            TraceBoot("Starting embedded Python service in background...");

            // Start Python service concurrently so the UI shows immediately.
            // The session loop will await it only when it's actually needed
            // (right before opening the MainWindow).
            var serviceStartTask = _pythonServiceHost.StartAsync();

            RunSessionLoop(serviceStartTask);
        }
        catch (Exception ex)
        {
            TraceBoot($"Fatal startup error: {ex}");
            MessageBox.Show(ex.ToString(), "Photobooth startup error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Stops the Python service before the application exits.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        TraceBoot("Application exit — stopping Python service");
        _pythonServiceHost?.Stop();
        _pythonServiceHost?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Main session loop. Accepts the Python service warm-up task so it can
    /// await readiness right before the heavy MainWindow is opened, not before
    /// the lightweight customer-input dialog.
    /// </summary>
    private async void RunSessionLoop(Task serviceStartTask)
    {
        while (true)
        {
            TraceBoot("Session loop: showing customer input dialog");

            var dialog = new CustomerInputDialog();
            var dialogResult = dialog.ShowDialog();

            if (dialogResult != true || string.IsNullOrWhiteSpace(dialog.CustomerName))
            {
                TraceBoot("Session loop: user cancelled dialog, exiting app");
                Shutdown(0);
                return;
            }

            // Await the service here — it has been warming up while the user
            // was typing their name, so this await is usually instant.
            if (!serviceStartTask.IsCompleted)
            {
                TraceBoot("Python service still starting — awaiting...");
            }

            try
            {
                await serviceStartTask.ConfigureAwait(true); // true = resume on UI thread
                TraceBoot("Python service ready");
            }
            catch (Exception ex)
            {
                TraceBoot($"Python service failed: {ex.Message}");
                MessageBox.Show(
                    $"Không thể khởi động Python service:\n{ex.Message}",
                    "Lỗi khởi động",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                // Continue anyway — image processing simply won't work.
            }

            TraceBoot($"Session loop: customer '{dialog.CustomerName}' confirmed, opening MainWindow");

            // Hiển thị loading dialog trước khi tạo MainWindow
            _loadingDialog = new LoadingDialog();
            _loadingDialog.UpdateStatus("Đang tải giao diện...");
            _loadingDialog.Show();

            // Force WPF render loading dialog trước
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            var mainWindow = new MainWindow(dialog.CustomerName, dialog.Settings);

            // Đóng loading dialog và hiển thị MainWindow
            _loadingDialog?.Close();
            _loadingDialog = null;

            void OnLoaded(object? s, RoutedEventArgs args)
            {
                mainWindow.Loaded -= OnLoaded;
                TraceBoot("Session loop: MainWindow Loaded event fired");
            }
            mainWindow.Loaded += OnLoaded;

            void OnEndSession(object? s, EventArgs args)
            {
                mainWindow.EndSessionRequested -= OnEndSession;
                TraceBoot("Session loop: EndSession received, closing MainWindow");
                mainWindow.Close();
            }
            mainWindow.EndSessionRequested += OnEndSession;

            void OnMainWindowClosed(object? s, EventArgs args)
            {
                mainWindow.Closed -= OnMainWindowClosed;
                TraceBoot("Session loop: MainWindow closed via X button, looping back to dialog");
                mainWindow.Close();
            }
            mainWindow.Closed += OnMainWindowClosed;

            mainWindow.ShowDialog();

            TraceBoot("Session loop: MainWindow closed, restarting session loop");
        }
    }

    private static void TraceBoot(string message)
    {
        try
        {
            Console.WriteLine(message);
            var logFolder = @"C:\photobooth\logs";
            Directory.CreateDirectory(logFolder);
            File.AppendAllText(
                Path.Combine(logFolder, "boot.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        TraceBoot($"Dispatcher exception: {e.Exception}");
        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        TraceBoot($"Domain exception: {e.ExceptionObject}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TraceBoot($"Task exception: {e.Exception}");
        e.SetObserved();
    }
}
