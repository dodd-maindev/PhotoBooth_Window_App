using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using Photobooth.Desktop.Models;
using System.Runtime.InteropServices;

namespace Photobooth.Desktop;

public partial class SettingsDialog : System.Windows.Window
{
    private readonly AppSettings _settings;
    private VideoCapture? _capture;
    private DispatcherTimer? _previewTimer;
    private bool _isClosing;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadSettings();
        StartCameraPreview();
    }

    private void StartCameraPreview()
    {
        // Try to find available webcam
        int deviceIndex = FindAvailableWebcam();
        if (deviceIndex < 0)
        {
            NoCameraText.Text = "Không tìm thấy camera";
            return;
        }

        try
        {
            _capture = new VideoCapture(deviceIndex);
            if (!_capture.IsOpened())
            {
                _capture.Dispose();
                NoCameraText.Text = "Không thể mở camera";
                return;
            }

            _previewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _previewTimer.Tick += PreviewTimer_Tick;
            _previewTimer.Start();
            NoCameraText.Visibility = Visibility.Collapsed;
        }
        catch
        {
            NoCameraText.Text = "Lỗi kết nối camera";
        }
    }

    private int FindAvailableWebcam()
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                using var test = new VideoCapture(i);
                if (test.IsOpened())
                {
                    test.Dispose();
                    return i;
                }
                test.Dispose();
            }
            catch { }
        }
        return -1;
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (_capture == null || !_capture.IsOpened() || _isClosing) return;

        try
        {
            using var frame = new Mat();
            if (!_capture.Read(frame) || frame.Empty()) return;

            // Flip horizontally (mirror)
            Cv2.Flip(frame, frame, FlipMode.Y);

            // Apply current brightness and contrast settings
            var processedFrame = ApplyCameraEffect(frame);

            // Convert to BitmapSource
            CameraPreviewImage.Source = MatToBitmapSource(processedFrame);
            processedFrame.Dispose();
        }
        catch
        {
            // Ignore frame errors
        }
    }

    private Mat ApplyCameraEffect(Mat input)
    {
        double brightness = BrightnessSlider.Value;
        double contrast = ContrastSlider.Value;

        if (brightness >= 0.95 && Math.Abs(contrast - 1.0) < 0.05)
        {
            return input.Clone();
        }

        var result = input.Clone();

        // Apply contrast first
        if (Math.Abs(contrast - 1.0) >= 0.05)
        {
            result.ConvertTo(result, -1, contrast, 0);
        }

        // Apply brightness
        if (brightness < 0.95)
        {
            result.ConvertTo(result, -1, 1, (brightness - 1.0) * 255);
        }

        return result;
    }

    private BitmapSource MatToBitmapSource(Mat mat)
    {
        int width = mat.Width;
        int height = mat.Height;
        int stride = width * 3; // BGR24 = 3 bytes per pixel
        byte[] pixels = new byte[height * stride];

        Marshal.Copy(mat.Data, pixels, 0, pixels.Length);

        // Convert BGR to RGB
        var rgbPixels = new byte[height * width * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int bgrIndex = y * stride + x * 3;
                int rgbaIndex = y * width * 4 + x * 4;

                rgbPixels[rgbaIndex] = pixels[bgrIndex + 2];     // R
                rgbPixels[rgbaIndex + 1] = pixels[bgrIndex + 1]; // G
                rgbPixels[rgbaIndex + 2] = pixels[bgrIndex];     // B
                rgbPixels[rgbaIndex + 3] = 255;                 // A
            }
        }

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, rgbPixels, width * 4);
        result.Freeze();
        return result;
    }

    private void LoadSettings()
    {
        CameraTypeComboBox.SelectedIndex = _settings.CameraType == CameraType.Fuji ? 1 : 0;

        FujiSaveFolderTextBox.Text = _settings.FujiSaveFolder;
        FujiWatchFolderTextBox.Text = _settings.FujiWatchFolder;
        FujiCameraNameTextBox.Text = _settings.FujiPreferredCameraName;
        FujiWebcamIndexTextBox.Text = _settings.FujiWebcamDeviceIndex.ToString();
        ProcessingFolderTextBox.Text = _settings.ProcessingFolder;
        OutputFolderTextBox.Text = _settings.OutputFolder;
        LogFolderTextBox.Text = _settings.LogFolder;

        CanonCameraNameTextBox.Text = _settings.CanonPreferredCameraName;
        CanonCameraIndexTextBox.Text = _settings.CanonCameraDeviceIndex.ToString();

        ApiBaseUrlTextBox.Text = _settings.ApiBaseUrl;
        ApiTimeoutTextBox.Text = _settings.ApiTimeoutSeconds.ToString();
        ApiRetriesTextBox.Text = _settings.ApiRetries.ToString();
        RetryDelayTextBox.Text = _settings.RetryDelayMilliseconds.ToString();

        AutoPrintCheckBox.IsChecked = _settings.EnableAutoPrint;

        // Load UiMode
        UiModeComboBox.SelectedIndex = _settings.UiMode == UiMode.Portrait ? 1 : 0;

        // Load Language
        LanguageComboBox.SelectedIndex = _settings.Language == AppLanguage.English ? 1 : 0;

        // Load FullScreen
        FullScreenCheckBox.IsChecked = _settings.EnableFullScreen;

        // Load Camera Brightness & Contrast
        BrightnessSlider.Value = _settings.CameraBrightness;
        ContrastSlider.Value = _settings.CameraContrast;
        UpdateBrightnessDisplay();
        UpdateContrastDisplay();

        UpdateFieldsVisibility();
    }

    private void CameraTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFieldsVisibility();
    }

    private void GuideButton_Click(object sender, RoutedEventArgs e)
    {
        var isFuji = GetSelectedCameraType() == CameraType.Fuji;
        var guideWindow = new GuideDialog(isFuji) { Owner = this };
        guideWindow.ShowDialog();
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrightnessValueText == null) return;
        UpdateBrightnessDisplay();
    }

    private void ContrastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ContrastValueText == null) return;
        UpdateContrastDisplay();
    }

    private void ResetCameraSettings_Click(object sender, RoutedEventArgs e)
    {
        BrightnessSlider.Value = 0.65;
        ContrastSlider.Value = 1.4;
    }

    private void UpdateBrightnessDisplay()
    {
        BrightnessValueText.Text = $"{(int)(BrightnessSlider.Value * 100)}%";
    }

    private void UpdateContrastDisplay()
    {
        ContrastValueText.Text = $"{ContrastSlider.Value:F1}x";
    }

    private void UpdateFieldsVisibility()
    {
        var isFuji = GetSelectedCameraType() == CameraType.Fuji;
        FujiSaveFolderTextBox.IsEnabled = isFuji;
        FujiWatchFolderTextBox.IsEnabled = isFuji;
        FujiCameraNameTextBox.IsEnabled = isFuji;
    }

    private CameraType GetSelectedCameraType()
    {
        if (CameraTypeComboBox.SelectedItem is ComboBoxItem item)
        {
            if (item.Tag is string tag && tag == "Fuji")
                return CameraType.Fuji;
        }
        return CameraType.Canon;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateAndSave()) return;
        SettingsSaved = true;
        DialogResult = true;
        Close();
    }

    public bool SettingsSaved { get; private set; }

    private bool ValidateAndSave()
    {
        if (string.IsNullOrWhiteSpace(ProcessingFolderTextBox.Text))
        {
            MessageBox.Show("Thư mục xử lý không được trống.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(OutputFolderTextBox.Text))
        {
            MessageBox.Show("Thư mục xuất không được trống.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _settings.CameraType = GetSelectedCameraType();

        _settings.FujiSaveFolder = FujiSaveFolderTextBox.Text.Trim();
        _settings.FujiWatchFolder = FujiWatchFolderTextBox.Text.Trim();
        _settings.FujiPreferredCameraName = FujiCameraNameTextBox.Text.Trim();
        if (int.TryParse(FujiWebcamIndexTextBox.Text.Trim(), out var fujiIndex))
        {
            _settings.FujiWebcamDeviceIndex = fujiIndex;
        }
        _settings.ProcessingFolder = ProcessingFolderTextBox.Text.Trim();
        _settings.OutputFolder = OutputFolderTextBox.Text.Trim();
        _settings.LogFolder = LogFolderTextBox.Text.Trim();

        _settings.CanonPreferredCameraName = CanonCameraNameTextBox.Text.Trim();

        if (int.TryParse(CanonCameraIndexTextBox.Text.Trim(), out var index))
        {
            _settings.CanonCameraDeviceIndex = index;
        }

        _settings.ApiBaseUrl = ApiBaseUrlTextBox.Text.Trim();
        if (int.TryParse(ApiTimeoutTextBox.Text.Trim(), out var timeout))
        {
            _settings.ApiTimeoutSeconds = timeout;
        }
        if (int.TryParse(ApiRetriesTextBox.Text.Trim(), out var retries))
        {
            _settings.ApiRetries = retries;
        }
        if (int.TryParse(RetryDelayTextBox.Text.Trim(), out var delay))
        {
            _settings.RetryDelayMilliseconds = delay;
        }

        _settings.EnableAutoPrint = AutoPrintCheckBox.IsChecked == true;

        // Save UiMode
        if (UiModeComboBox.SelectedItem is ComboBoxItem uiItem)
        {
            if (uiItem.Tag is string uiTag && uiTag == "Portrait")
                _settings.UiMode = UiMode.Portrait;
            else
                _settings.UiMode = UiMode.Landscape;
        }

        // Save Language
        if (LanguageComboBox.SelectedItem is ComboBoxItem langItem)
        {
            if (langItem.Tag is string langTag && langTag == "English")
                _settings.Language = AppLanguage.English;
            else
                _settings.Language = AppLanguage.Vietnamese;
        }

        // Save FullScreen
        _settings.EnableFullScreen = FullScreenCheckBox.IsChecked == true;

        // Save Camera Brightness & Contrast
        _settings.CameraBrightness = BrightnessSlider.Value;
        _settings.CameraContrast = ContrastSlider.Value;

        try
        {
            _settings.Save(AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể lưu file cài đặt: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        return true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        _previewTimer?.Stop();
        _capture?.Dispose();
        base.OnClosing(e);
    }
}
