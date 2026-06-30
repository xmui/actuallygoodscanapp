using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.Core.Imaging;

/// <summary>
/// Detects multiple separate items (photos / documents) placed on the flatbed in a single scan and
/// returns a cropped image for each. Uses connected-component labelling on the content mask, keeps
/// components whose bounding box is large enough to be a real item, and merges heavily overlapping
/// boxes. When only one item is found (or detection is unreliable) it returns a single content crop.
/// </summary>
public static class MultiPhotoSplitter
{
    /// <summary>
    /// Splits the scan into one image per detected item. Each returned image is an independent crop
    /// of <paramref name="source"/>. Caller owns (and must dispose) the returned images.
    /// </summary>
    public static List<Image<Rgba32>> Split(
        Image<Rgba32> source,
        double minAreaFraction = 0.015,
        double marginFraction = 0.01)
    {
        ArgumentNullException.ThrowIfNull(source);
        var boxes = DetectItemRects(source, minAreaFraction);

        var results = new List<Image<Rgba32>>();
        if (boxes.Count <= 1)
        {
            // Fall back to a single auto-crop so behaviour is sensible for a single item / blank page.
            results.Add(AutoCropProcessor.AutoCrop(source, marginFraction));
            return results;
        }

        foreach (var box in boxes)
        {
            var r = AutoCropProcessor.AddMargin(box, source.Width, source.Height, marginFraction);
            results.Add(source.Clone(c => c.Crop(r)));
        }

        return results;
    }

    /// <summary>
    /// Returns the bounding rectangles (source coordinates) of detected items, ordered top-to-bottom
    /// then left-to-right (natural reading order).
    /// </summary>
    public static List<Rectangle> DetectItemRects(Image<Rgba32> source, double minAreaFraction = 0.015)
    {
        var mask = ImageAnalysis.BuildForegroundMask(source, maxDimension: 1000);
        int w = mask.Width, h = mask.Height;
        int minComponentArea = (int)(w * (double)h * minAreaFraction);

        // Close gaps so each photo becomes one solid blob (photos often have light regions that don't
        // threshold as foreground, which would otherwise fragment or hide an item).
        int radius = Math.Max(2, Math.Min(w, h) / 80);
        var fg = ImageAnalysis.Close(mask.Foreground, w, h, radius);

        var labels = new int[w * h];
        var stack = new Stack<int>();
        var boxes = new List<Rectangle>();

        for (int start = 0; start < labels.Length; start++)
        {
            if (!fg[start] || labels[start] != 0)
            {
                continue;
            }

            // Flood fill this component (4-connectivity is enough and cheaper than 8 here).
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            int area = 0;
            stack.Push(start);
            labels[start] = 1;
            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                int x = idx % w;
                int y = idx / w;
                area++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                if (x > 0) Push(labels, fg, stack, idx - 1);
                if (x < w - 1) Push(labels, fg, stack, idx + 1);
                if (y > 0) Push(labels, fg, stack, idx - w);
                if (y < h - 1) Push(labels, fg, stack, idx + w);
            }

            int bw = maxX - minX + 1;
            int bh = maxY - minY + 1;
            // Ignore the whole-platen blob (a near-full-frame component is the background, not an item).
            bool fillsFrame = bw >= w * 0.96 && bh >= h * 0.96;
            if (bw * bh >= minComponentArea && !fillsFrame)
            {
                boxes.Add(AutoCropProcessor.ScaleRect(
                    new Rectangle(minX, minY, bw, bh), mask.Scale, source.Width, source.Height));
            }
        }

        var merged = MergeOverlapping(boxes);
        merged.Sort((a, b) =>
        {
            // Group into rows (~5% of height) then order left-to-right within a row.
            int rowTol = Math.Max(1, source.Height / 20);
            if (Math.Abs(a.Y - b.Y) > rowTol)
            {
                return a.Y.CompareTo(b.Y);
            }
            return a.X.CompareTo(b.X);
        });
        return merged;
    }

    private static void Push(int[] labels, bool[] fg, Stack<int> stack, int idx)
    {
        if (labels[idx] == 0 && fg[idx])
        {
            labels[idx] = 1;
            stack.Push(idx);
        }
    }

    /// <summary>Merges rectangles that overlap significantly so a single item isn't split in two.</summary>
    internal static List<Rectangle> MergeOverlapping(List<Rectangle> boxes)
    {
        var list = new List<Rectangle>(boxes);
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    if (Overlaps(list[i], list[j]))
                    {
                        list[i] = Rectangle.Union(list[i], list[j]);
                        list.RemoveAt(j);
                        changed = true;
                        break;
                    }
                }
                if (changed) break;
            }
        }
        return list;
    }

    private static bool Overlaps(Rectangle a, Rectangle b)
    {
        var inter = Rectangle.Intersect(a, b);
        if (inter.Width <= 0 || inter.Height <= 0)
        {
            return false;
        }
        long interArea = (long)inter.Width * inter.Height;
        long minArea = Math.Min((long)a.Width * a.Height, (long)b.Width * b.Height);
        return interArea * 100 >= minArea * 20; // >=20% of the smaller box overlaps
    }
}
