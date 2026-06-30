using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.Core.Imaging;

/// <summary>
/// Straightens (deskews) a scan. The skew angle is estimated with a projection-profile method:
/// the correction angle that, when applied to the content pixels, produces the sharpest horizontal
/// projection histogram (text lines / photo edges collapsing into rows) is the best straightening
/// angle. Cheap, robust for documents, and platform-independent.
/// </summary>
public static class DeskewProcessor
{
    public const double MaxAngleDegrees = 15.0;

    /// <summary>
    /// Estimates the correction angle (degrees) that should be applied to straighten the image.
    /// Positive / negative follows ImageSharp's <c>Rotate</c> convention so the value can be passed
    /// straight to <see cref="Deskew"/>. Returns 0 when no reliable skew is found.
    /// </summary>
    public static double EstimateSkewDegrees(Image<Rgba32> source, double stepDegrees = 0.25)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mask = ImageAnalysis.BuildForegroundMask(source, maxDimension: 1000);

        // Collect foreground points, sub-sampled to bound the cost.
        var points = new List<(int X, int Y)>();
        int total = mask.ForegroundCount();
        if (total < 50)
        {
            return 0.0;
        }

        int stride = Math.Max(1, total / 40000);
        int seen = 0;
        for (int y = 0; y < mask.Height; y++)
        {
            int baseIdx = y * mask.Width;
            for (int x = 0; x < mask.Width; x++)
            {
                if (mask.Foreground[baseIdx + x])
                {
                    if (seen % stride == 0)
                    {
                        points.Add((x, y));
                    }
                    seen++;
                }
            }
        }

        double bestAngle = 0.0;
        double bestScore = double.NegativeInfinity;
        int diagonal = (int)Math.Ceiling(Math.Sqrt((mask.Width * (double)mask.Width) + (mask.Height * (double)mask.Height)));

        for (double angle = -MaxAngleDegrees; angle <= MaxAngleDegrees + 1e-9; angle += stepDegrees)
        {
            double rad = angle * Math.PI / 180.0;
            double sin = Math.Sin(rad);
            double cos = Math.Cos(rad);

            var hist = new int[(2 * diagonal) + 1];
            foreach (var (x, y) in points)
            {
                // y-component of rotating the point by `angle` (ImageSharp y-down convention).
                int yp = (int)Math.Round((x * sin) + (y * cos)) + diagonal;
                if (yp >= 0 && yp < hist.Length)
                {
                    hist[yp]++;
                }
            }

            double score = 0;
            foreach (var c in hist)
            {
                score += (double)c * c;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestAngle = angle;
            }
        }

        // Snap tiny angles to zero to avoid needless rotation of already-straight scans.
        return Math.Abs(bestAngle) < 0.3 ? 0.0 : bestAngle;
    }

    /// <summary>
    /// Returns a straightened copy of the image. Background-coloured fill is used for the corners
    /// exposed by rotation. If no meaningful skew is detected the original is returned (cloned).
    /// </summary>
    public static Image<Rgba32> Deskew(Image<Rgba32> source, double? knownAngle = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        double angle = knownAngle ?? EstimateSkewDegrees(source);
        if (Math.Abs(angle) < 0.3)
        {
            return source.Clone();
        }

        var fill = EstimateFillColor(source);
        var result = source.Clone();
        // Rotate first (this exposes transparent corners), then flood the new corners with the
        // background colour so they don't read as black content on the next pass or in exports.
        result.Mutate(c => c.Rotate((float)angle).BackgroundColor(fill));
        return result;
    }

    /// <summary>Background fill colour estimated from border luminance (white-ish or black-ish).</summary>
    private static Color EstimateFillColor(Image<Rgba32> source)
    {
        var mask = ImageAnalysis.BuildForegroundMask(source, maxDimension: 200);
        // Re-derive an approximate background brightness from a tiny copy.
        using var tiny = source.Clone(c => c.Resize(Math.Min(source.Width, 64), Math.Min(source.Height, 64)));
        long sum = 0;
        int count = 0;
        tiny.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                // sample only the edge pixels of the tiny image
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
