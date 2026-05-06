using System.ComponentModel;
using Photobooth.Desktop.Models;

namespace Photobooth.Desktop.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private Models.AppLanguage _currentLanguage = Models.AppLanguage.Vietnamese;
    private readonly Dictionary<string, Dictionary<Models.AppLanguage, string>> _translations;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Models.AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnPropertyChanged(nameof(CurrentLanguage));
                OnPropertyChanged(string.Empty);
            }
        }
    }

    private LocalizationService()
    {
        _translations = new Dictionary<string, Dictionary<AppLanguage, string>>
        {
            // App Title
            ["AppTitle"] = new() { { AppLanguage.Vietnamese, "JOLI FILM - SELFBOOTH HA NOI" }, { AppLanguage.English, "JOLI FILM - SELFBOOTH" } },
            ["BrandName"] = new() { { AppLanguage.Vietnamese, "JOLI FILM" }, { AppLanguage.English, "JOLI FILM" } },
            ["AppSubtitle"] = new() { { AppLanguage.Vietnamese, "SELFBOOTH HANOI" }, { AppLanguage.English, "SELFBOOTH HANOI" } },
            ["Address"] = new() { { AppLanguage.Vietnamese, "34 ngõ 58 Đào Tấn, Ba Đình, Hà Nội" }, { AppLanguage.English, "34 ngõ 58 Đào Tấn, Ba Đình, Hà Nội" } },

            // Camera
            ["LivePreview"] = new() { { AppLanguage.Vietnamese, "Live Camera Preview" }, { AppLanguage.English, "Live Camera Preview" } },
            ["LivePreviewDesc"] = new() { { AppLanguage.Vietnamese, "Hình ảnh trực tiếp từ máy ảnh" }, { AppLanguage.English, "Live feed from camera" } },
            ["ProcessedPreview"] = new() { { AppLanguage.Vietnamese, "Processed Preview" }, { AppLanguage.English, "Processed Preview" } },
            ["ProcessedPreviewDesc"] = new() { { AppLanguage.Vietnamese, "Ảnh đã áp dụng filter" }, { AppLanguage.English, "Image with filter applied" } },
            ["CapturedPhoto"] = new() { { AppLanguage.Vietnamese, "Ảnh Đã Chụp" }, { AppLanguage.English, "PHOTO" } },
            ["LivePreviewPortrait"] = new() { { AppLanguage.Vietnamese, "LIVE PREVIEW" }, { AppLanguage.English, "LIVE PREVIEW" } },

            // Filters
            ["Filters"] = new() { { AppLanguage.Vietnamese, "Filters" }, { AppLanguage.English, "Filters" } },
            ["FilterDesc"] = new() { { AppLanguage.Vietnamese, "Chọn filter cho ảnh chụp" }, { AppLanguage.English, "FILTER for photos" } },
            ["SelectFilter"] = new() { { AppLanguage.Vietnamese, "CHỌN FILTER" }, { AppLanguage.English, "FILTER" } },

            // Filter Names
            ["FilterGrayscale"] = new() { { AppLanguage.Vietnamese, "Grayscale" }, { AppLanguage.English, "Grayscale" } },
            ["FilterBlur"] = new() { { AppLanguage.Vietnamese, "Blur" }, { AppLanguage.English, "Blur" } },
            ["FilterVintage"] = new() { { AppLanguage.Vietnamese, "Vintage" }, { AppLanguage.English, "Vintage" } },
            ["FilterBeauty"] = new() { { AppLanguage.Vietnamese, "Beauty" }, { AppLanguage.English, "Beauty" } },
            ["FilterGrayscaleDesc"] = new() { { AppLanguage.Vietnamese, "Ảnh đen trắng rõ nét" }, { AppLanguage.English, "Black and white image" } },
            ["FilterBlurDesc"] = new() { { AppLanguage.Vietnamese, "Làm mờ nhẹ để tạo hiệu ứng" }, { AppLanguage.English, "Light blur for effect" } },
            ["FilterVintageDesc"] = new() { { AppLanguage.Vietnamese, "Tông cổ điển, ấm và mềm" }, { AppLanguage.English, "Classic, warm and soft tone" } },
            ["FilterBeautyDesc"] = new() { { AppLanguage.Vietnamese, "Làm mịn da cơ bản" }, { AppLanguage.English, "Basic skin smoothing" } },

            // Actions
            ["Capture"] = new() { { AppLanguage.Vietnamese, "CHỤP ẢNH" }, { AppLanguage.English, "TAKE PHOTO" } },
            ["Delete"] = new() { { AppLanguage.Vietnamese, "XÓA" }, { AppLanguage.English, "DELETE" } },
            ["EndSession"] = new() { { AppLanguage.Vietnamese, "KẾT THÚC" }, { AppLanguage.English, "FINISH" } },
            ["Print"] = new() { { AppLanguage.Vietnamese, "IN ẢNH" }, { AppLanguage.English, "PRINT" } },
            ["PrintFour"] = new() { { AppLanguage.Vietnamese, "IN 4 ẢNH" }, { AppLanguage.English, "PRINT 4" } },

            // Customer Input
            ["WELCOME TO JOLI FILMTitle"] = new() { { AppLanguage.Vietnamese, "CHÀO MỪNG ĐẾN VỚI JOLI FILM" }, { AppLanguage.English, "WELCOME TO JOLI FILM" } },
            ["WELCOME TO JOLI FILMSubtitle"] = new() { { AppLanguage.Vietnamese, "JOLI FILM SELFBOOTH" }, { AppLanguage.English, "JOLI FILM SELFBOOTH" } },
            ["EnterNameTitle"] = new() { { AppLanguage.Vietnamese, "NHẬP TÊN CỦA BẠN" }, { AppLanguage.English, "ENTER YOUR NAME" } },
            ["NamePlaceholder"] = new() { { AppLanguage.Vietnamese, "Tên khách hàng..." }, { AppLanguage.English, "Customer name..." } },
            ["Continue"] = new() { { AppLanguage.Vietnamese, "TIẾP TỤC" }, { AppLanguage.English, "CONTINUE" } },
            ["Guest"] = new() { { AppLanguage.Vietnamese, "Khách" }, { AppLanguage.English, "Guest" } },

            // Settings
            ["Settings"] = new() { { AppLanguage.Vietnamese, "CÀI ĐẶT" }, { AppLanguage.English, "SETTINGS" } },
            ["CameraSettings"] = new() { { AppLanguage.Vietnamese, "Cài đặt Camera" }, { AppLanguage.English, "Camera Settings" } },
            ["Language"] = new() { { AppLanguage.Vietnamese, "Ngôn ngữ" }, { AppLanguage.English, "Language" } },
            ["Vietnamese"] = new() { { AppLanguage.Vietnamese, "Tiếng Việt" }, { AppLanguage.English, "Vietnamese" } },
            ["English"] = new() { { AppLanguage.Vietnamese, "Tiếng Anh" }, { AppLanguage.English, "English" } },
            ["CameraType"] = new() { { AppLanguage.Vietnamese, "Loại Camera" }, { AppLanguage.English, "Camera Type" } },
            ["Canon"] = new() { { AppLanguage.Vietnamese, "Canon" }, { AppLanguage.English, "Canon" } },
            ["Fuji"] = new() { { AppLanguage.Vietnamese, "Fujifilm" }, { AppLanguage.English, "Fujifilm" } },
            ["WatchFolder"] = new() { { AppLanguage.Vietnamese, "Thư mục theo dõi" }, { AppLanguage.English, "Watch Folder" } },
            ["PrintSettings"] = new() { { AppLanguage.Vietnamese, "Cài đặt In Ảnh" }, { AppLanguage.English, "Print Settings" } },
            ["AutoPrint"] = new() { { AppLanguage.Vietnamese, "Tự động in sau chụp" }, { AppLanguage.English, "Auto print after capture" } },
            ["UiMode"] = new() { { AppLanguage.Vietnamese, "Chế độ hiển thị" }, { AppLanguage.English, "Display Mode" } },
            ["Landscape"] = new() { { AppLanguage.Vietnamese, "Ngang (Landscape)" }, { AppLanguage.English, "Landscape" } },
            ["Portrait"] = new() { { AppLanguage.Vietnamese, "Dọc (Portrait)" }, { AppLanguage.English, "Portrait" } },
            ["Save"] = new() { { AppLanguage.Vietnamese, "LƯU" }, { AppLanguage.English, "SAVE" } },
            ["Cancel"] = new() { { AppLanguage.Vietnamese, "HỦY" }, { AppLanguage.English, "CANCEL" } },

            // Status
            ["Ready"] = new() { { AppLanguage.Vietnamese, "Sẵn sàng." }, { AppLanguage.English, "Ready." } },
            ["Processing"] = new() { { AppLanguage.Vietnamese, "Đang xử lý..." }, { AppLanguage.English, "Processing..." } },
            ["ProcessingFilter"] = new() { { AppLanguage.Vietnamese, "Đang xử lý filter..." }, { AppLanguage.English, "Applying filter..." } },
            ["PhotoCaptured"] = new() { { AppLanguage.Vietnamese, "Đã chụp ảnh thành công!" }, { AppLanguage.English, "Photo captured successfully!" } },
            ["PhotoSaved"] = new() { { AppLanguage.Vietnamese, "Đã lưu ảnh!" }, { AppLanguage.English, "Photo saved!" } },
            ["CameraReady"] = new() { { AppLanguage.Vietnamese, "Camera live view đang chạy." }, { AppLanguage.English, "Camera live view running." } },
            ["CameraError"] = new() { { AppLanguage.Vietnamese, "Lỗi camera!" }, { AppLanguage.English, "Camera error!" } },
            ["CameraWaiting"] = new() { { AppLanguage.Vietnamese, "Đang chờ camera..." }, { AppLanguage.English, "Waiting for camera..." } },
            ["CameraNotFound"] = new() { { AppLanguage.Vietnamese, "Không tìm thấy camera." }, { AppLanguage.English, "Camera not found." } },
            ["SessionEnded"] = new() { { AppLanguage.Vietnamese, "Đã kết thúc phiên." }, { AppLanguage.English, "Session ended." } },
            ["PhotoPrinted"] = new() { { AppLanguage.Vietnamese, "Đã in ảnh thành công!" }, { AppLanguage.English, "Photo printed successfully!" } },
            ["PrintError"] = new() { { AppLanguage.Vietnamese, "Lỗi khi in ảnh!" }, { AppLanguage.English, "Error printing photo!" } },
            ["HistoryCleared"] = new() { { AppLanguage.Vietnamese, "Đã xóa lịch sử." }, { AppLanguage.English, "History cleared." } },
            ["Customer"] = new() { { AppLanguage.Vietnamese, "Khách" }, { AppLanguage.English, "Customer" } },
            ["Filter"] = new() { { AppLanguage.Vietnamese, "Filter" }, { AppLanguage.English, "Filter" } },

            // Canon specific
            ["CanonHint"] = new() { { AppLanguage.Vietnamese, "Bấm nút vật lý trên máy ảnh để chụp" }, { AppLanguage.English, "Press physical button on camera to capture" } },
            ["CanonWaiting"] = new() { { AppLanguage.Vietnamese, "Canon: Chờ chụp từ máy ảnh..." }, { AppLanguage.English, "Canon: Waiting for camera..." } },

            // Fuji specific
            ["FujiHint"] = new() { { AppLanguage.Vietnamese, "Bấm nút chụp trên máy ảnh Fujifilm" }, { AppLanguage.English, "Press capture button on Fujifilm camera" } },
            ["FujiWaiting"] = new() { { AppLanguage.Vietnamese, "Fujifilm: Chờ chụp từ máy ảnh..." }, { AppLanguage.English, "Fujifilm: Waiting for camera..." } },

            // Errors
            ["ErrorNoFrame"] = new() { { AppLanguage.Vietnamese, "Chưa có frame camera để chụp." }, { AppLanguage.English, "No camera frame available to capture." } },
            ["ErrorCapture"] = new() { { AppLanguage.Vietnamese, "Lỗi khi chụp ảnh!" }, { AppLanguage.English, "Error capturing photo!" } },

            // Loading
            ["LoadingTitle"] = new() { { AppLanguage.Vietnamese, "Đang xử lý ảnh..." }, { AppLanguage.English, "Processing image..." } },
        };
    }

    public string this[string key]
    {
        get
        {
            if (_translations.TryGetValue(key, out var dict))
            {
                if (dict.TryGetValue(_currentLanguage, out var value))
                    return value;
                if (dict.TryGetValue(Models.AppLanguage.Vietnamese, out var fallback))
                    return fallback;
            }
            return $"[{key}]";
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
