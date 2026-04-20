using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Desktop.Services;

public static class ImageSourceFactory
{
    private static readonly object _loadLock = new();

    public static ImageSource? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        lock (_loadLock)
        {
            var bytes = File.ReadAllBytes(filePath);
            using var memoryStream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(memoryStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            if (frame is null)
            {
                return null;
            }

            if (frame.CanFreeze)
            {
                frame.Freeze();
            }

            return frame;
        }
    }
}