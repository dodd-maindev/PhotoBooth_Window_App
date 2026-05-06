using System.Windows;
using System.Windows.Controls;
using Photobooth.Desktop.Models;

namespace Photobooth.Desktop;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadSettings();
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
}
