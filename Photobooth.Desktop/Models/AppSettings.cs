using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Photobooth.Desktop.Models;

public enum CameraType
{
    Canon = 0,
    Fuji = 1
}

public enum UiMode
{
    Landscape = 0,
    Portrait = 1
}

public enum AppLanguage
{
    Vietnamese = 0,
    English = 1
}

public sealed class AppSettings
{
    public string WatchFolder { get; set; } = @"C:\photos";

    public string ProcessingFolder { get; set; } = @"C:\photobooth\incoming";

    public string OutputFolder { get; set; } = @"C:\photobooth\output";

    public string LogFolder { get; set; } = @"C:\photobooth\logs";

    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:8000";

    public CameraType CameraType { get; set; } = CameraType.Canon;

    public string CanonPreferredCameraName { get; set; } = "EOS Webcam Utility";

    public int CanonCameraDeviceIndex { get; set; } = -1;

    public int ApiTimeoutSeconds { get; set; } = 60;

    public int ApiRetries { get; set; } = 3;

    public int RetryDelayMilliseconds { get; set; } = 700;

    public bool EnableAutoPrint { get; set; }

    public string FujiSaveFolder { get; set; } = @"C:\photobooth\fuji";

    public string FujiWatchFolder { get; set; } = @"C:\photobooth\fuji_incoming";

    public string FujiPreferredCameraName { get; set; } = "FUJIFILM X Webcam";

    public int FujiWebcamDeviceIndex { get; set; } = -1;

    public int FujiLiveWebcamDeviceIndex { get; set; } = 0;

    /// <summary>Name of the PC webcam used for Fuji live preview (empty = auto-detect index 0).</summary>
    public string LiveWebcamPreferredName { get; set; } = "";

    /// <summary>
    /// Preferred camera name for generic webcam mode (used when not Canon EOS Utility tether).
    /// Falls back to CameraDeviceIndex if empty.
    /// </summary>
    public string PreferredCameraName { get; set; } = "";

    /// <summary>
    /// Webcam device index for generic camera preview (used when not Canon EOS Utility tether).
    /// -1 = auto-detect.
    /// </summary>
    public int CameraDeviceIndex { get; set; } = -1;

    /// <summary>
    /// UI layout mode: Landscape (0) or Portrait (1).
    /// </summary>
    public UiMode UiMode { get; set; } = UiMode.Landscape;

    /// <summary>
    /// Application language: Vietnamese (0) or English (1).
    /// </summary>
    public AppLanguage Language { get; set; } = AppLanguage.Vietnamese;

    /// <summary>
    /// Enable fullscreen mode on startup.
    /// </summary>
    public bool EnableFullScreen { get; set; }

    public static AppSettings Load(string baseDirectory)
    {
        var settings = new AppSettings();
        var filePath = Path.Combine(baseDirectory, "appsettings.json");

        if (!File.Exists(filePath))
        {
            return settings;
        }

        using var stream = File.OpenRead(filePath);
        var loaded = JsonSerializer.Deserialize(stream, typeof(AppSettings), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        }) as AppSettings;

        return loaded ?? settings;
    }

    public void Save(string baseDirectory)
    {
        var filePath = Path.Combine(baseDirectory, "appsettings.json");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(filePath, json);
    }

    public void CopyFrom(AppSettings other)
    {
        WatchFolder = other.WatchFolder;
        ProcessingFolder = other.ProcessingFolder;
        OutputFolder = other.OutputFolder;
        LogFolder = other.LogFolder;
        ApiBaseUrl = other.ApiBaseUrl;
        CameraType = other.CameraType;
        CanonPreferredCameraName = other.CanonPreferredCameraName;
        CanonCameraDeviceIndex = other.CanonCameraDeviceIndex;
        ApiTimeoutSeconds = other.ApiTimeoutSeconds;
        ApiRetries = other.ApiRetries;
        RetryDelayMilliseconds = other.RetryDelayMilliseconds;
        EnableAutoPrint = other.EnableAutoPrint;
        FujiSaveFolder = other.FujiSaveFolder;
        FujiWatchFolder = other.FujiWatchFolder;
        FujiPreferredCameraName = other.FujiPreferredCameraName;
        FujiWebcamDeviceIndex = other.FujiWebcamDeviceIndex;
        FujiLiveWebcamDeviceIndex = other.FujiLiveWebcamDeviceIndex;
        LiveWebcamPreferredName = other.LiveWebcamPreferredName;
        PreferredCameraName = other.PreferredCameraName;
        CameraDeviceIndex = other.CameraDeviceIndex;
        UiMode = other.UiMode;
        Language = other.Language;
        EnableFullScreen = other.EnableFullScreen;
    }
}
