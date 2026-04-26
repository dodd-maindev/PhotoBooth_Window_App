using System.IO;

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

        System.IO.File.Copy(sourceImagePath, destPath, overwrite: true);
        System.IO.File.AppendAllText(@"C:\photobooth\debug.log", $"[{DateTime.Now:HH:mm:ss}] Saved original to: {destPath}{Environment.NewLine}");
        return destPath;
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

        File.Copy(sourceImagePath, destPath, overwrite: true);
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
