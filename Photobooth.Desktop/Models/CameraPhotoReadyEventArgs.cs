namespace Photobooth.Desktop.Models;

public sealed class CameraPhotoReadyEventArgs : EventArgs
{
    public CameraPhotoReadyEventArgs(string originalPath, string copiedPath)
    {
        OriginalPath = originalPath;
        CopiedPath = copiedPath;
    }

    public string OriginalPath { get; }

    public string CopiedPath { get; }
}