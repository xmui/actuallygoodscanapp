using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.Core.Imaging;

/// <summary>
/// Auto-crops a scan to its content. Detects the content bounding box against the estimated
/// background (works for both light flatbed glass and dark ADF backing), adds a small margin and
/// crops. Also supports cropping to a fixed physical size centred on the detected content.
/// </summary>
public static class AutoCropProcessor
{
    /// <summary>
    /// Returns a new image cropped to detected content with a small margin. If the page looks
    /// blank (no content found), the original image is returned unchanged (cloned).
    /// </summary>
    public static Image<Rgba32> AutoCrop(Image<Rgba32> source, double marginFraction = 0.01)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rect = DetectContentRect(source);
        if (rect is null)
        {
            return source.Clone();
        }

        var r = AddMargin(rect.Value, source.Width, source.Height, marginFraction);
        return source.Clone(c => c.Crop(r));
    }

    /// <summary>Detects the content rectangle in source-image coordinates, or null if blank.</summary>
    public static Rectangle? DetectContentRect(Image<Rgba32> source)
    {
        var mask = ImageAnalysis.BuildForegroundMask(source);
        var bounds = ImageAnalysis.ContentBounds(mask);
        if (bounds is null)
        {
            return null;
        }

        return ScaleRect(bounds.Value, mask.Scale, source.Width, source.Height);
    }

    /// <summary>
    /// Crops to a fixed physical size (in inches at the given dpi), centred on the detected
    /// content, clamped to the image bounds.
    /// </summary>
    public static Image<Rgba32> CropToFixedSize(Image<Rgba32> source, FixedSizePreset preset, int dpi)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (wIn, hIn) = preset.ToInches();
        int targetW = Math.Min(source.Width, Math.Max(1, (int)Math.Round(wIn * dpi)));
        int targetH = Math.Min(source.Height, Math.Max(1, (int)Math.Round(hIn * dpi)));

        var content = DetectContentRect(source);
        int cx, cy;
        if (content is { } c)
        {
            cx = c.X + (c.Width / 2);
            cy = c.Y + (c.Height / 2);
        }
        else
        {
            cx = source.Width / 2;
            cy = source.Height / 2;
        }

        int x = Math.Clamp(cx - (targetW / 2), 0, source.Width - targetW);
        int y = Math.Clamp(cy - (targetH / 2), 0, source.Height - targetH);
        return source.Clone(c => c.Crop(new Rectangle(x, y, targetW, targetH)));
    }

    internal static Rectangle ScaleRect(Rectangle r, double scale, int maxW, int maxH)
    {
        int x = (int)Math.Floor(r.X * scale);
        int y = (int)Math.Floor(r.Y * scale);
        int w = (int)Math.Ceiling(r.Width * scale);
        int h = (int)Math.Ceiling(r.Height * scale);
        x = Math.Clamp(x, 0, Math.Max(0, maxW - 1));
        y = Math.Clamp(y, 0, Math.Max(0, maxH - 1));
        w = Math.Clamp(w, 1, maxW - x);
        h = Math.Clamp(h, 1, maxH - y);
        return new Rectangle(x, y, w, h);
    }

    internal static Rectangle AddMargin(Rectangle r, int maxW, int maxH, double marginFraction)
    {
        int mx = (int)Math.Round(r.Width * marginFraction);
        int my = (int)Math.Round(r.Height * marginFraction);
        int x = Math.Max(0, r.X - mx);
        int y = Math.Max(0, r.Y - my);
        int w = Math.Min(maxW - x, r.Width + (2 * mx));
        int h = Math.Min(maxH - y, r.Height + (2 * my));
        return new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
    }
}
