using System.IO;
using System.Text.Json;

namespace Photobooth.Desktop.Models;

public sealed class AppSettings
{
    public string WatchFolder { get; set; } = @"C:\photos";

    public string ProcessingFolder { get; set; } = @"C:\photobooth\incoming";

    public string OutputFolder { get; set; } = @"C:\photobooth\output";

    public string LogFolder { get; set; } = @"C:\photobooth\logs";

    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:8000";

    public string PreferredCameraName { get; set; } = "EOS Webcam Utility";

    public int CameraDeviceIndex { get; set; } = -1;

    public int ApiTimeoutSeconds { get; set; } = 60;

    public int ApiRetries { get; set; } = 3;

    public int RetryDelayMilliseconds { get; set; } = 700;

    public bool EnableAutoPrint { get; set; }

    public static AppSettings Load(string baseDirectory)
    {
        var settings = new AppSettings();
        var filePath = Path.Combine(baseDirectory, "appsettings.json");

        if (!File.Exists(filePath))
        {
            return settings;
        }

        using var stream = File.OpenRead(filePath);
        var loaded = JsonSerializer.Deserialize<AppSettings>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return loaded ?? settings;
    }
}