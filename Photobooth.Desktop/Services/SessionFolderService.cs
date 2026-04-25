using System.IO;

namespace Photobooth.Desktop.Services;

/// <summary>
/// Builds and resolves the structured session folder hierarchy.
/// Layout: {root}/{customerName}/{yyyy-MM-dd}/raw|filter/{filterKey}
/// </summary>
public sealed class SessionFolderService
{
    private readonly string _sessionsRoot;

    /// <summary>Initialises the service with the root sessions directory.</summary>
    public SessionFolderService(string sessionsRoot)
    {
        _sessionsRoot = sessionsRoot;
    }

    /// <summary>
    /// Returns and ensures the raw folder for a given customer + date.
    /// Output: {root}/{customer}/{date}/raw
    /// </summary>
    public string EnsureRawFolder(string customerName, DateTime sessionDate)
    {
        var path = Path.Combine(
            _sessionsRoot,
            Sanitize(customerName),
            sessionDate.ToString("yyyy-MM-dd"),
            "raw");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Returns and ensures the filter cache folder for a given filter key.
    /// Output: {root}/{customer}/{date}/filter/{filterKey}
    /// </summary>
    public string EnsureFilterFolder(string customerName, DateTime sessionDate, string filterKey)
    {
        var path = Path.Combine(
            _sessionsRoot,
            Sanitize(customerName),
            sessionDate.ToString("yyyy-MM-dd"),
            "filter",
            filterKey);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Sanitises a customer name for use as a directory component.</summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = name.Trim();
        foreach (var c in invalid)
        {
            result = result.Replace(c, '_');
        }
        return result.Replace("..", "_").Replace("/", "_").Replace("\\", "_");
    }
}
