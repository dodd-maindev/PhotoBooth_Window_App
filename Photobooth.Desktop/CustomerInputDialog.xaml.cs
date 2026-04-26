using System.Windows;
using System.Windows.Input;
using Photobooth.Desktop.Models;

namespace Photobooth.Desktop;

public partial class CustomerInputDialog : Window
{
    public string? CustomerName { get; private set; }
    public AppSettings Settings { get; private set; }

    public CustomerInputDialog()
    {
        InitializeComponent();
        Settings = AppSettings.Load(AppContext.BaseDirectory);
        CustomerNameTextBox.Focus();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var name = CustomerNameTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                "Vui lòng nhập tên khách hàng.",
                "Thông báo",
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
