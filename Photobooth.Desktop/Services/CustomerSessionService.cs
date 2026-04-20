using System.IO;

namespace Photobooth.Desktop.Services;

public sealed class CustomerSessionService
{
    private readonly string _rootPhotoFolder;

    public CustomerSessionService(string rootPhotoFolder)
    {
        _rootPhotoFolder = rootPhotoFolder;
    }

    public string? CurrentSessionPath { get; private set; }
    public string? CurrentCustomerName { get; private set; }

    public string CreateSessionFolder(string customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            throw new ArgumentException("Tên khách hàng không được trống.", nameof(customerName));
        }

        var sanitizedName = SanitizeFolderName(customerName);
        var timestamp = DateTime.Now.ToString("HH-mm-ss_dd-MM-yyyy");
        var folderName = $"{sanitizedName}/{timestamp}";

        var sessionPath = Path.Combine(_rootPhotoFolder, folderName);

        Directory.CreateDirectory(sessionPath);

        CurrentSessionPath = sessionPath;
        CurrentCustomerName = customerName;

        return sessionPath;
    }

    public (string WatchFolder, string ProcessingFolder, string OutputFolder) GetSessionPaths()
    {
        if (string.IsNullOrEmpty(CurrentSessionPath))
        {
            throw new InvalidOperationException("Chưa tạo session. Gọi CreateSessionFolder trước.");
        }

        var watchFolder = Path.Combine(CurrentSessionPath, "camera");
        var processingFolder = Path.Combine(CurrentSessionPath, "processing");
        var outputFolder = Path.Combine(CurrentSessionPath, "output");

        Directory.CreateDirectory(watchFolder);
        Directory.CreateDirectory(processingFolder);
        Directory.CreateDirectory(outputFolder);

        return (watchFolder, processingFolder, outputFolder);
    }

    public void ClearSession()
    {
        CurrentSessionPath = null;
        CurrentCustomerName = null;
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = name.Trim();
        foreach (var c in invalid)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        return sanitized.Replace("..", "_").Replace("/", "_").Replace("\\", "_");
    }
}
