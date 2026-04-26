using System.Windows;

namespace Photobooth.Desktop;

public partial class LoadingDialog : Window
{
    public LoadingDialog()
    {
        InitializeComponent();
    }

    public void UpdateStatus(string status)
    {
        StatusText.Text = status;
    }
}
