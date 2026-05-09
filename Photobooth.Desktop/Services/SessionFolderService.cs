using System.IO;
using OpenCvSharp;

namespace Photobooth.Desktop.Services;

public class SessionFolderService
{
    private readonly string _sessionsBaseFolder;
    private string? _currentSessionFolder;
    private string? _currentOriginalFolder;
    private string? _currentFilterFolder;
    private int _photoCounter;
    private string? _currentFilterName;

    public SessionFolderService(string sessionsBaseFolder = @"C:\photobooth\sessions")
    {
        _sessionsBaseFolder = sessionsBaseFolder;
    }

    public string CurrentSessionFolder => _currentSessionFolder ?? string.Empty;
    public string CurrentOriginalFolder => _currentOriginalFolder ?? string.Empty;
    public int PhotoCounter => _photoCounter;

    public void StartNewSession(string customerName)
    {
        _photoCounter = 0;
        _currentFilterName = null;
        _currentFilterFolder = null;

        var dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
        var timeFolder = DateTime.Now.ToString("HHmm");
        var sessionFolderName = $"{customerName}_{timeFolder}";

        _currentSessionFolder = Path.Combine(_sessionsBaseFolder, dateFolder, sessionFolderName);
        _currentOriginalFolder = Path.Combine(_currentSessionFolder, "original");

        Directory.CreateDirectory(_sessionsBaseFolder);
        Directory.CreateDirectory(_currentSessionFolder);
        Directory.CreateDirectory(_currentOriginalFolder);

        System.IO.File.AppendAllText(@"C:\photobooth\debug.log", $"[{DateTime.Now:HH:mm:ss}] Session started: {_currentSessionFolder}{Environment.NewLine}");
    }

    public string? SaveOriginalPhoto(string sourceImagePath)
    {
        if (string.IsNullOrEmpty(_currentOriginalFolder))
        {
            System.IO.File.AppendAllText(@"C:\photobooth\debug.log", $"[{DateTime.Now:HH:mm:ss}] SaveOriginalPhoto: _currentOriginalFolder is null{Environment.NewLine}");
            return null;
        }

        if (!System.IO.File.Exists(sourceImagePath))
        {
            System.IO.File.AppendAllText(@"C:\photobooth\debug.log", $"[{DateTime.Now:HH:mm:ss}] SaveOriginalPhoto: source file not found: {sourceImagePath}{Environment.NewLine}");
            return null;
        }

        _photoCounter++;
        var fileName = $"photobooth_{_photoCounter}{System.IO.Path.GetExtension(sourceImagePath)}";
        var destPath = System.IO.Path.Combine(_currentOriginalFolder, fileName);

        // Flip image horizontally to match the mirrored preview
        FlipAndSaveImage(sourceImagePath, destPath);

        System.IO.File.AppendAllText(@"C:\photobooth\debug.log", $"[{DateTime.Now:HH:mm:ss}] Saved original to: {destPath}{Environment.NewLine}");
        return destPath;
    }

    private void FlipAndSaveImage(string sourcePath, string destPath)
    {
        try
        {
            using var img = Cv2.ImRead(sourcePath, ImreadModes.Unchanged);
            if (img.Empty())
            {
                // Fallback: just copy
                System.IO.File.Copy(sourcePath, destPath, overwrite: true);
                return;
            }

            // Flip horizontally (mirror) to match preview
            using var flipped = new Mat();
            Cv2.Flip(img, flipped, FlipMode.Y);

            // Determine format and save
            var ext = Path.GetExtension(destPath).ToLowerInvariant();
            var encoding = ext switch
            {
                ".png" => ".png",
                ".jpg" or ".jpeg" => ".jpg",
                ".bmp" => ".bmp",
                _ => ".png"
            };

            var finalPath = Path.ChangeExtension(destPath, encoding);
            var success = Cv2.ImWrite(finalPath, flipped);

            if (!success)
            {
                System.IO.File.AppendAllText(@"C:\photobooth\debug.log", $"[{DateTime.Now:HH:mm:ss}] ImWrite failed for {sourcePath}, fallback to copy{Environment.NewLine}");
                System.IO.File.Copy(sourcePath, destPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(@"C:\photobooth\debug.log", $"[{DateTime.Now:HH:mm:ss}] FlipAndSaveImage error: {ex.Message}{Environment.NewLine}");
            // Fallback: just copy
            System.IO.File.Copy(sourcePath, destPath, overwrite: true);
        }
    }

    public void SetCurrentFilter(string filterName)
    {
        if (_currentFilterName == filterName)
            return;

        _currentFilterName = filterName;

        if (string.IsNullOrEmpty(_currentSessionFolder))
            return;

        _currentFilterFolder = Path.Combine(_currentSessionFolder, "filter", filterName);
        Directory.CreateDirectory(_currentFilterFolder!);
    }

    public string? SaveFilteredPhoto(string sourceImagePath, string filterName)
    {
        if (string.IsNullOrEmpty(_currentSessionFolder) || !File.Exists(sourceImagePath))
            return null;

        var filterFolder = Path.Combine(_currentSessionFolder, "filter", filterName);
        Directory.CreateDirectory(filterFolder);

        var fileName = $"photobooth_{_photoCounter}{Path.GetExtension(sourceImagePath)}";
        var destPath = Path.Combine(filterFolder, fileName);

        // Flip image horizontally to match preview
        FlipAndSaveImage(sourceImagePath, destPath);

        return destPath;
    }

    public string GetSessionInfo()
    {
        if (string.IsNullOrEmpty(_currentSessionFolder))
            return "Chưa có session";

        var dirInfo = new DirectoryInfo(_currentSessionFolder);
        return $"Session: {dirInfo.Name} | Ảnh: {_photoCounter}";
    }

    public void EndSession()
    {
        _currentSessionFolder = null;
        _currentOriginalFolder = null;
        _currentFilterFolder = null;
        _currentFilterName = null;
        _photoCounter = 0;
    }
}
