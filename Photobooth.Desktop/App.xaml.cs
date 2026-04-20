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
            TraceBoot("Starting embedded Python service...");
            await _pythonServiceHost.StartAsync();
            TraceBoot("Python service ready");

            RunSessionLoop();
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

    private void RunSessionLoop()
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

            TraceBoot($"Session loop: customer '{dialog.CustomerName}' confirmed, opening MainWindow");

            var mainWindow = new MainWindow(dialog.CustomerName, _settings!);

            void OnLoaded(object? s, RoutedEventArgs args)
            {
                mainWindow.Loaded -= OnLoaded;
                TraceBoot("Session loop: MainWindow Loaded event fired");
            }
            mainWindow.Loaded += OnLoaded;

            int closeReason = 0;

            void OnEndSession(object? s, EventArgs args)
            {
                mainWindow.EndSessionRequested -= OnEndSession;
                TraceBoot("Session loop: EndSession received, closing MainWindow");
                closeReason = 1;
                mainWindow.Close();
            }
            mainWindow.EndSessionRequested += OnEndSession;

            void OnMainWindowClosed(object? s, EventArgs args)
            {
                mainWindow.Closed -= OnMainWindowClosed;
                TraceBoot("Session loop: MainWindow closed via X button, exiting app");
                if (closeReason == 0)
                    closeReason = 2;
            }
            mainWindow.Closed += OnMainWindowClosed;

            mainWindow.ShowDialog();

            TraceBoot("Session loop: MainWindow closed, restarting session loop");

            if (closeReason == 2)
            {
                Shutdown(0);
                return;
            }
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
