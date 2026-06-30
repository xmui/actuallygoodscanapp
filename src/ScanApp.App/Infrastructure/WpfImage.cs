using System.IO;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.App.Infrastructure;

/// <summary>Bridges ImageSharp images (used by the core pipeline) to WPF <see cref="BitmapSource"/>.</summary>
public static class WpfImage
{
    /// <summary>Converts an ImageSharp image to a frozen, cross-thread-safe WPF bitmap.</summary>
    public static BitmapSource ToBitmapSource(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var ms = new MemoryStream();
        image.SaveAsBmp(ms);
        ms.Position = 0;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze(); // safe to hand to the UI thread from a background task
        return bmp;
    }

    /// <summary>Converts to a downscaled thumbnail for the filmstrip (keeps memory low).</summary>
    public static BitmapSource ToThumbnail(Image<Rgba32> image, int maxDimension = 220)
    {
        ArgumentNullException.ThrowIfNull(image);
        int longSide = Math.Max(image.Width, image.Height);
        if (longSide <= maxDimension)
        {
            return ToBitmapSource(image);
        }

        double scale = (double)maxDimension / longSide;
        int w = Math.Max(1, (int)(image.Width * scale));
        int h = Math.Max(1, (int)(image.Height * scale));
        using var thumb = image.Clone(c => c.Resize(w, h));
        return ToBitmapSource(thumb);
    }
}
