using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.Core.Imaging;

/// <summary>
/// Text-document cleanup, à la VueScan's document mode: whitens a dingy/grey background, sharpens
/// text, and optionally converts to crisp black-and-white. Applied in place.
/// </summary>
public static class DocumentCleanup
{
    /// <summary>
    /// Whiten + sharpen (+ optional black-and-white). <paramref name="blackAndWhite"/> produces a
    /// 1-bit-look thresholded page (smallest, cleanest for text).
    /// </summary>
    public static void Apply(Image<Rgba32> image, bool blackAndWhite)
    {
        ArgumentNullException.ThrowIfNull(image);

        // Levels: push highlights to white and deepen text, so the page background goes clean white.
        const byte black = 50;
        const byte white = 175;
        var lut = new byte[256];
        double scale = 255.0 / (white - black);
        for (int v = 0; v < 256; v++)
        {
            lut[v] = (byte)Math.Clamp((int)Math.Round((v - black) * scale), 0, 255);
        }

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    row[x] = new Rgba32(lut[p.R], lut[p.G], lut[p.B], p.A);
                }
            }
        });

        image.Mutate(c => c.GaussianSharpen(1.1f));

        if (blackAndWhite)
        {
            image.Mutate(c => c.BinaryThreshold(0.6f));
        }
    }
}
