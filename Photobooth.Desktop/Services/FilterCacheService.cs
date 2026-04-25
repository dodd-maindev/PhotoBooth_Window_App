using System.IO;

namespace Photobooth.Desktop.Services;

/// <summary>
/// Manages the on-disk filter result cache.
/// Each filtered result is stored as: {filterFolder}/{rawFileBaseName}.png
/// So re-applying the same filter to the same photo is instant (no API call).
/// </summary>
public sealed class FilterCacheService
{
    /// <summary>
    /// Returns the expected cached output path for a given raw file + filter folder.
    /// The file may or may not exist yet.
    /// </summary>
    public string GetCachePath(string filterFolder, string rawFilePath)
    {
        var baseName = Path.GetFileNameWithoutExtension(rawFilePath);
        return Path.Combine(filterFolder, baseName + ".png");
    }

    /// <summary>
    /// Returns true and sets <paramref name="cachedPath"/> if a cached result already exists.
    /// Returns false if the filter has not yet been applied to this photo.
    /// </summary>
    public bool TryGetCached(string filterFolder, string rawFilePath, out string cachedPath)
    {
        cachedPath = GetCachePath(filterFolder, rawFilePath);
        return File.Exists(cachedPath);
    }
}
