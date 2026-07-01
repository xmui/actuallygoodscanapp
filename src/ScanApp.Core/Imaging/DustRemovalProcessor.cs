using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanApp.Core.Imaging;

/// <summary>
/// Software dust &amp; hair removal, à la Photoshop's "Dust &amp; Scratches": a small-radius median
/// filter is computed, but a pixel is only replaced by the median where it deviates from it by more
/// than a threshold. That targets isolated specks and thin hairs while leaving broad areas and strong
/// edges intact. Honest limitation: this is a purely software approximation — it has no infrared dust
/// channel like some dedicated film scanners, so extreme cases won't be perfectly removed.
/// </summary>
public static class DustRemovalProcessor
{
    /// <summary>
    /// Removes dust/hair in place. <paramref name="strength"/> is 1..100 (higher = larger radius and
    /// lower threshold = more aggressive). A value &lt;= 0 is a no-op.
    /// </summary>
    public static void Remove(Image<Rgba32> image, int strength)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (strength <= 0)
        {
            return;
        }

        int w = image.Width, h = image.Height;
        int radius = Math.Clamp(1 + (strength / 50), 1, 3);
        int threshold = Math.Clamp(45 - (int)(strength * 0.37), 8, 45);

        // Snapshot channels so the filter reads from the original, not partially-updated pixels.
        var r = new byte[w * h];
        var g = new byte[w * h];
        var b = new byte[w * h];
        var a = new byte[w * h];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                int bi = y * w;
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    r[bi + x] = p.R; g[bi + x] = p.G; b[bi + x] = p.B; a[bi + x] = p.A;
                }
            }
        });

        int win = (2 * radius) + 1;
        var buf = new int[win * win];

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                int bi = y * w;
                for (int x = 0; x < w; x++)
                {
                    byte mr = Median(r, w, h, x, y, radius, buf);
                    byte mg = Median(g, w, h, x, y, radius, buf);
                    byte mb = Median(b, w, h, x, y, radius, buf);

                    int idx = bi + x;
                    int dev = Math.Max(Math.Abs(r[idx] - mr), Math.Max(Math.Abs(g[idx] - mg), Math.Abs(b[idx] - mb)));
                    if (dev > threshold)
                    {
                        row[x] = new Rgba32(mr, mg, mb, a[idx]);
                    }
                }
            }
        });
    }

    private static byte Median(byte[] channel, int w, int h, int cx, int cy, int radius, int[] buf)
    {
        int n = 0;
        int y0 = Math.Max(0, cy - radius), y1 = Math.Min(h - 1, cy + radius);
        int x0 = Math.Max(0, cx - radius), x1 = Math.Min(w - 1, cx + radius);
        for (int y = y0; y <= y1; y++)
        {
            int bi = y * w;
            for (int x = x0; x <= x1; x++)
            {
                buf[n++] = channel[bi + x];
            }
        }
        Array.Sort(buf, 0, n);
        return (byte)buf[n / 2];
    }
}
