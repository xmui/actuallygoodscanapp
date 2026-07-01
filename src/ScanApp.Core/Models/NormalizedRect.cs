using SixLabors.ImageSharp;

namespace ScanApp.Core.Models;

/// <summary>
/// A crop rectangle expressed in normalized 0..1 coordinates so it is independent of the image's
/// pixel size (a non-destructive crop that is stored per page and applied only at export time).
/// </summary>
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    /// <summary>The full image (no crop).</summary>
    public static readonly NormalizedRect Full = new(0, 0, 1, 1);

    public bool IsFull => X <= 0 && Y <= 0 && Width >= 1 && Height >= 1;

    /// <summary>Clamps the rectangle into the 0..1 unit square, keeping a non-zero size.</summary>
    public NormalizedRect Clamped()
    {
        double x = Math.Clamp(X, 0, 1);
        double y = Math.Clamp(Y, 0, 1);
        double w = Math.Clamp(Width, 0, 1 - x);
        double h = Math.Clamp(Height, 0, 1 - y);
        if (w <= 0) w = Math.Min(0.01, 1 - x);
        if (h <= 0) h = Math.Min(0.01, 1 - y);
        return new NormalizedRect(x, y, w, h);
    }

    /// <summary>Converts to a pixel rectangle for an image of the given size, clamped to bounds.</summary>
    public Rectangle ToPixels(int imageWidth, int imageHeight)
    {
        var c = Clamped();
        int x = (int)Math.Round(c.X * imageWidth);
        int y = (int)Math.Round(c.Y * imageHeight);
        int w = (int)Math.Round(c.Width * imageWidth);
        int h = (int)Math.Round(c.Height * imageHeight);
        x = Math.Clamp(x, 0, Math.Max(0, imageWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, imageHeight - 1));
        w = Math.Clamp(w, 1, imageWidth - x);
        h = Math.Clamp(h, 1, imageHeight - y);
        return new Rectangle(x, y, w, h);
    }

    public static NormalizedRect FromPixels(Rectangle r, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return Full;
        }
        return new NormalizedRect(
            (double)r.X / imageWidth,
            (double)r.Y / imageHeight,
            (double)r.Width / imageWidth,
            (double)r.Height / imageHeight).Clamped();
    }
}
