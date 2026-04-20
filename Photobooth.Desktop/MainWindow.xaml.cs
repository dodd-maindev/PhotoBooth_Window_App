using System;
using System.Windows;
using System.Windows.Input;
using Photobooth.Desktop.Models;
using Photobooth.Desktop.Services;
using Photobooth.Desktop.ViewModels;

namespace Photobooth.Desktop;

public partial class MainWindow : Window
{
    public MainViewModel? ViewModel { get; }

    public MainWindow(string customerName, AppSettings settings)
    {
        InitializeComponent();

        var rootPhotoFolder = @"C:\photobooth\Photo";
        var sessionService = new CustomerSessionService(rootPhotoFolder);
        var sessionPath = sessionService.CreateSessionFolder(customerName);
        var (watchFolder, processingFolder, outputFolder) = sessionService.GetSessionPaths();

        ViewModel = new MainViewModel(settings, sessionService, customerName, watchFolder, processingFolder, outputFolder);
        DataContext = ViewModel;

        Loaded += MainWindow_Loaded;
        Closing += (_, _) => ViewModel?.Dispose();
        ViewModel.EndSessionRequested += (_, _) =>
        {
            EndSessionRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? EndSessionRequested;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        try
        {
            await ViewModel.InitializeAsync();
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