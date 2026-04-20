using System.Windows;
using System.Windows.Input;

namespace Photobooth.Desktop;

public partial class CustomerInputDialog : Window
{
    public string? CustomerName { get; private set; }

    public CustomerInputDialog()
    {
        InitializeComponent();
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
}
