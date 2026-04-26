using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Photobooth.Desktop;

public partial class GuideDialog : Window
{
    private readonly bool _isFuji;

    public GuideDialog(bool isFuji)
    {
        InitializeComponent();
        _isFuji = isFuji;
        LoadContent();
    }

    private void LoadContent()
    {
        if (_isFuji)
        {
            LoadFujiGuide();
        }
        else
        {
            LoadCanonGuide();
        }
    }

    private void LoadCanonGuide()
    {
        HeaderText.Text = "HƯỚNG DẪN CÀI ĐẶT CANON";

        ContentPanel.Children.Add(CreateSection("1. Tải và cài đặt Canon EOS Utility",
            @"Phần mềm này cho phép máy ảnh Canon hoạt động như webcam thông qua kết nối USB.
• EOS Utility hỗ trợ chụp từ xa và truyền ảnh trực tiếp.
• Yêu cầu: .NET Framework 4.7.1 hoặc cao hơn.",
            "https://vn.canon/vi/support/0200730902"));

        ContentPanel.Children.Add(CreateSection("2. Cập nhật firmware máy ảnh",
            @"Để EOS Utility hoạt động đúng, máy ảnh Canon cần firmware mới nhất:
• Truy cập trang hỗ trợ Canon và tìm model máy ảnh của bạn.
• Tải firmware mới nhất từ trang chính thức của Canon.
• Làm theo hướng dẫn trong file cài đặt firmware để cập nhật.
• Một số model yêu cầu firmware version cụ thể (ví dụ: EOS R5 cần v2.0.0+)"));

        ContentPanel.Children.Add(CreateSection("3. Kết nối máy ảnh",
            @"• Kết nối máy ảnh với máy tính qua cáp USB.
• Bật máy ảnh ở chế độ chụp ảnh (Photo mode).
• Mở ứng dụng EOS Utility trên máy tính.
• Ứng dụng sẽ tự động nhận diện máy ảnh đã kết nối."));

        ContentPanel.Children.Add(CreateSection("4. Cài đặt trong ứng dụng Photobooth",
            @"• Trong mục 'Loại máy ảnh', chọn 'Canon (EOS Utility)'.
• Nếu máy ảnh không được nhận diện, hãy thử nhập tên thiết bị hoặc chỉ mục camera thủ công.
• Đảm bảo chế độ 'Live View' đang bật trên máy ảnh."));
    }

    private void LoadFujiGuide()
    {
        HeaderText.Text = "HƯỚNG DẪN CÀI ĐẶT FUJIFILM";

        ContentPanel.Children.Add(CreateSection("1. Tải và cài đặt Fujifilm X Webcam",
            "Phần mềm này cho phép máy ảnh Fujifilm hoạt động như webcam.",
            "https://fujifilm-x.com/en-us/support/download/software/"));

        ContentPanel.Children.Add(CreateSection("2. Cài đặt Fujifilm Tether shooting plug-in",
            @"Plug-in này cho phép chụp ảnh từ xa và tự động lưu ảnh vào thư mục máy tính.
• Tải plug-in phù hợp với máy ảnh Fujifilm của bạn.
• Cài đặt theo hướng dẫn của Fujifilm.",
            "https://fujifilm-x.com/en-us/support/download/software/"));

        ContentPanel.Children.Add(CreateSection("3. Cài đặt firmware mới nhất",
            @"Đảm bảo firmware máy ảnh Fujifilm đã được cập nhật:
• Kiểm tra phiên bản firmware hiện tại trên máy ảnh.
• Tải firmware mới nhất từ trang chính thức Fujifilm.
• Làm theo hướng dẫn để cập nhật firmware."));

        ContentPanel.Children.Add(CreateSection("4. Cấu hình thư mục lưu ảnh",
            @"• Trong Tether App, thiết lập thư mục lưu ảnh đã chụp.
• Trong ứng dụng Photobooth, nhập đường dẫn thư mục đó vào mục 'Thư mục Fuji Save'.
• Đảm bảo ứng dụng có quyền ghi vào thư mục này."));

        ContentPanel.Children.Add(CreateSection("5. Kết nối và sử dụng",
            @"• Kết nối máy ảnh Fujifilm với máy tính qua cáp USB.
• Bật máy ảnh và mở X Webcam.
• Mở Tether App và bắt đầu phiên chụp.
• Ảnh sẽ tự động được chuyển vào thư mục đã cấu hình."));
    }

    private StackPanel CreateSection(string title, string content, string? link = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(titleBlock);

        var contentBlock = new TextBlock
        {
            Text = content,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(contentBlock);

        if (!string.IsNullOrEmpty(link))
        {
            var linkButton = new Button
            {
                Content = "Tải xuống tại đây",
                Style = (Style)FindResource("LinkButtonStyle"),
                Tag = link,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            linkButton.Click += LinkButton_Click;
            panel.Children.Add(linkButton);
        }

        return panel;
    }

    private void LinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể mở liên kết: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
