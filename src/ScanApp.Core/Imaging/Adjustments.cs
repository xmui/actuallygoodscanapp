using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.Core.Imaging;

/// <summary>Basic tonal adjustments (brightness / contrast / auto-levels), à la VueScan.</summary>
public static class Adjustments
{
    /// <summary>
    /// Applies brightness and contrast in place. Both are −100..100 where 0 = no change; positive
    /// brightens / increases contrast, negative the opposite.
    /// </summary>
    public static void Apply(Image<Rgba32> image, int brightness, int contrast)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (brightness == 0 && contrast == 0)
        {
            return;
        }

        image.Mutate(ctx =>
        {
            if (brightness != 0)
            {
                ctx.Brightness(1f + (brightness / 100f));
            }
            if (contrast != 0)
            {
                ctx.Contrast(1f + (contrast / 100f));
            }
        });
    }

    /// <summary>
    /// Stretches each channel so its darkest value maps to 0 and brightest to 255 — a one-click
    /// contrast/exposure fix for flat scans. No-op for an already full-range or blank image.
    /// </summary>
    public static void AutoLevels(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int w = image.Width, h = image.Height;

        byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    if (p.R < minR) minR = p.R; if (p.R > maxR) maxR = p.R;
                    if (p.G < minG) minG = p.G; if (p.G > maxG) maxG = p.G;
                    if (p.B < minB) minB = p.B; if (p.B > maxB) maxB = p.B;
                }
            }
        });

        var lutR = BuildLut(minR, maxR);
        var lutG = BuildLut(minG, maxG);
        var lutB = BuildLut(minB, maxB);
        if (lutR is null && lutG is null && lutB is null)
        {
            return;
        }

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    row[x] = new Rgba32(
                        lutR?[p.R] ?? p.R,
                        lutG?[p.G] ?? p.G,
                        lutB?[p.B] ?? p.B,
                        p.A);
                }
            }
        });
    }

    private static byte[]? BuildLut(byte min, byte max)
    {
        if (max <= min || (min == 0 && max == 255))
        {
            return null; // nothing to stretch on this channel
        }

        double scale = 255.0 / (max - min);
        var lut = new byte[256];
        for (int v = 0; v < 256; v++)
        {
            lut[v] = (byte)Math.Clamp((int)Math.Round((v - min) * scale), 0, 255);
        }
        return lut;
    }
}
