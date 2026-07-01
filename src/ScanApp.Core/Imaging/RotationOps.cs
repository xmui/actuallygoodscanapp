using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.Core.Imaging;

/// <summary>Fine (arbitrary-angle) rotation used for Lightroom-style straightening.</summary>
public static class RotationOps
{
    /// <summary>
    /// Rotates by an arbitrary angle (degrees, clockwise) but keeps the original pixel dimensions:
    /// the image is rotated with a background fill and then centre-cropped back to its original size.
    /// This keeps the crop overlay and edit pipeline working with stable dimensions; the exposed
    /// corners are filled and can be cropped out by the user. A tiny angle is a no-op (clone).
    /// </summary>
    public static Image<Rgba32> RotateKeepSize(Image<Rgba32> source, double degrees)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Math.Abs(degrees) < 0.05)
        {
            return source.Clone();
        }

        int w = source.Width, h = source.Height;
        var fill = EstimateFill(source);

        var rotated = source.Clone(c => c.Rotate((float)degrees).BackgroundColor(fill));

        // Centre-crop back to the original size.
        int x = Math.Max(0, (rotated.Width - w) / 2);
        int y = Math.Max(0, (rotated.Height - h) / 2);
        int cw = Math.Min(w, rotated.Width);
        int ch = Math.Min(h, rotated.Height);
        rotated.Mutate(c => c.Crop(new Rectangle(x, y, cw, ch)));

        if (rotated.Width != w || rotated.Height != h)
        {
            rotated.Mutate(c => c.Resize(w, h));
        }
        return rotated;
    }

    private static Color EstimateFill(Image<Rgba32> source)
    {
        using var tiny = source.Clone(c => c.Resize(Math.Min(source.Width, 48), Math.Min(source.Height, 48)));
        long sum = 0;
        int count = 0;
        tiny.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                bool edgeRow = y == 0 || y == accessor.Height - 1;
                for (int x = 0; x < row.Length; x++)
                {
                    if (edgeRow || x == 0 || x == row.Length - 1)
                    {
                        sum += ImageAnalysis.Luminance(row[x]);
                        count++;
                    }
                }
            }
        });
        byte b = count == 0 ? (byte)255 : (byte)(sum / count);
        return new Color(new Rgba32(b, b, b, 255));
    }
}
