using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Desktop;

public partial class PrintConfirmDialog : Window
{
    public bool ShouldPrintNow { get; private set; }

    public PrintConfirmDialog(string previewImagePath, int photoCount, string? customerName)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        if (File.Exists(previewImagePath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(previewImagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                PreviewImage.Source = bitmap;
            }
            catch
            {
                // Ignore if image cannot be loaded
            }
        }

        PhotoCountText.Text = photoCount == 4
            ? "Chuẩn bị in 4 ảnh"
            : $"Chuẩn bị in {photoCount} ảnh";
    }

    private void ConfirmPrint_Click(object sender, RoutedEventArgs e)
    {
        ShouldPrintNow = false;
        DialogResult = true;
    }

    private void PrintNow_Click(object sender, RoutedEventArgs e)
    {
        ShouldPrintNow = true;
        DialogResult = true;
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
