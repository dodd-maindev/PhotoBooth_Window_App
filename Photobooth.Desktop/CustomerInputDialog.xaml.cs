using System.Windows;
using System.Windows.Input;
using Photobooth.Desktop.Models;
using Photobooth.Desktop.Services;

namespace Photobooth.Desktop;

public partial class CustomerInputDialog : Window
{
    public string? CustomerName { get; private set; }
    public AppSettings Settings { get; private set; }
    private readonly LocalizationService _loc = LocalizationService.Instance;

    public CustomerInputDialog()
    {
        InitializeComponent();
        Settings = AppSettings.Load(AppContext.BaseDirectory);

        // Apply language setting
        _loc.CurrentLanguage = Settings.Language;
        ApplyLocalization();

        CustomerNameTextBox.Focus();
    }

    private void ApplyLocalization()
    {
        AddressText.Text = _loc["Address"];
        WelcomeTitleText.Text = _loc["WELCOME TO JOLI FILMTitle"];
        WelcomeSubtitleText.Text = _loc["WELCOME TO JOLI FILMSubtitle"];
        NameLabelText.Text = _loc["EnterNameTitle"];
        CustomerNameTextBox.Text = "";
        StartButtonText.Text = _loc["Continue"];
        SettingsButtonText.Text = _loc["Settings"];
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var name = CustomerNameTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                _loc["EnterNameTitle"],
                _loc["AppTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            CustomerNameTextBox.Focus();
            CustomerNameTextBox.SelectAll();
            return;
        }

        CustomerName = name;
        DialogResult = true;
        Close();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(Settings);
        dialog.Owner = this;
        dialog.ShowDialog();

        if (dialog.SettingsSaved)
        {
            Settings = AppSettings.Load(AppContext.BaseDirectory);
            _loc.CurrentLanguage = Settings.Language;
            ApplyLocalization();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            StartButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void CustomerNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            StartButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CustomerNameTextBox.Focus();
    }
}
