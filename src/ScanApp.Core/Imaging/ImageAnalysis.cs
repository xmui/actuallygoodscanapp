using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.Core.Imaging;

/// <summary>
/// Low-level pixel analysis shared by the crop / deskew / split processors: background estimation
/// and foreground (content) mask extraction. All routines are fully managed (ImageSharp) so they
/// run and are unit-tested on any platform.
/// </summary>
public static class ImageAnalysis
{
    /// <summary>A binary content mask plus the geometry needed to map it back to the source image.</summary>
    public sealed class Mask
    {
        public required bool[] Foreground { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }

        /// <summary>Source pixels per mask pixel (>= 1 when the source was downscaled for speed).</summary>
        public required double Scale { get; init; }

        public bool At(int x, int y) => Foreground[(y * Width) + x];

        public int ForegroundCount()
        {
            int n = 0;
            foreach (var f in Foreground)
            {
                if (f) n++;
            }
            return n;
        }
    }

    public static byte Luminance(Rgba32 p) =>
        (byte)((p.R * 0.299) + (p.G * 0.587) + (p.B * 0.114));

    /// <summary>
    /// Builds a foreground mask. Background luminance is estimated from the image border (the
    /// part most likely to be empty glass / feeder backing); a pixel is foreground when its
    /// luminance differs from the background by more than <paramref name="threshold"/>.
    /// The mask may be computed on a downscaled copy (bounded by <paramref name="maxDimension"/>)
    /// for speed; <see cref="Mask.Scale"/> records the ratio back to source coordinates.
    /// </summary>
    public static Mask BuildForegroundMask(Image<Rgba32> source, int threshold = 38, int maxDimension = 1400)
    {
        ArgumentNullException.ThrowIfNull(source);

        double scale = 1.0;
        int longSide = Math.Max(source.Width, source.Height);
        Image<Rgba32>? working = null;
        Image<Rgba32> img;
        if (longSide > maxDimension)
        {
            scale = (double)longSide / maxDimension;
            int w = Math.Max(1, (int)Math.Round(source.Width / scale));
            int h = Math.Max(1, (int)Math.Round(source.Height / scale));
            working = source.Clone(c => c.Resize(w, h));
            img = working;
            // recompute exact scale from chosen dimensions
            scale = (double)source.Width / w;
        }
        else
        {
            img = source;
        }

        int width = img.Width;
        int height = img.Height;
        var lum = new byte[width * height];
        var opaque = new bool[width * height];
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int baseIdx = y * width;
                for (int x = 0; x < width; x++)
                {
                    var p = row[x];
                    lum[baseIdx + x] = Luminance(p);
                    opaque[baseIdx + x] = p.A >= 128;
                }
            }
        });

        byte background = EstimateBorderLuminance(lum, width, height);

        var fg = new bool[width * height];
        for (int i = 0; i < fg.Length; i++)
        {
            // Transparent pixels (e.g. corners exposed by a rotation) are background, not content.
            fg[i] = opaque[i] && Math.Abs(lum[i] - background) > threshold;
        }

        working?.Dispose();

        return new Mask { Foreground = fg, Width = width, Height = height, Scale = scale };
    }

    /// <summary>Median luminance of a thin strip around the image border.</summary>
    public static byte EstimateBorderLuminance(byte[] lum, int width, int height)
    {
        int strip = Math.Max(1, Math.Min(width, height) / 50);
        var samples = new List<byte>();
        for (int y = 0; y < height; y++)
        {
            bool topOrBottom = y < strip || y >= height - strip;
            int baseIdx = y * width;
            for (int x = 0; x < width; x++)
            {
                if (topOrBottom || x < strip || x >= width - strip)
                {
                    samples.Add(lum[baseIdx + x]);
                }
            }
        }

        if (samples.Count == 0)
        {
            return 255;
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }

    /// <summary>
    /// Content bounding box on the mask, noise-tolerant: a row/column counts as content only when
    /// at least <paramref name="minFraction"/> of it is foreground. Returns null for a blank mask.
    /// </summary>
    public static Rectangle? ContentBounds(Mask mask, double minFraction = 0.004)
    {
        int w = mask.Width, h = mask.Height;
        var colCount = new int[w];
        var rowCount = new int[h];
        for (int y = 0; y < h; y++)
        {
            int baseIdx = y * w;
            for (int x = 0; x < w; x++)
            {
                if (mask.Foreground[baseIdx + x])
                {
                    colCount[x]++;
                    rowCount[y]++;
                }
            }
        }

        int rowThreshold = Math.Max(2, (int)(w * minFraction));
        int colThreshold = Math.Max(2, (int)(h * minFraction));

        int top = -1, bottom = -1, left = -1, right = -1;
        for (int y = 0; y < h; y++)
        {
            if (rowCount[y] >= rowThreshold) { if (top < 0) top = y; bottom = y; }
        }
        for (int x = 0; x < w; x++)
        {
            if (colCount[x] >= colThreshold) { if (left < 0) left = x; right = x; }
        }

        if (top < 0 || left < 0)
        {
            return null;
        }

        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }
}
