using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.Core.Tests;

/// <summary>Helpers to synthesise scans for the imaging tests.</summary>
internal static class TestImages
{
    public static readonly Color White = Color.White;
    public static readonly Color Black = Color.Black;

    /// <summary>White canvas with a single filled black rectangle of content.</summary>
    public static Image<Rgba32> WhiteWith(int width, int height, Rectangle content)
    {
        var img = new Image<Rgba32>(width, height, White);
        img.Mutate(c => c.Fill(Black, new SixLabors.ImageSharp.Drawing.RectangularPolygon(
            content.X, content.Y, content.Width, content.Height)));
        return img;
    }

    /// <summary>White canvas with several black rectangles (e.g. multiple photos on the glass).</summary>
    public static Image<Rgba32> WhiteWithMany(int width, int height, IEnumerable<Rectangle> rects)
    {
        var img = new Image<Rgba32>(width, height, White);
        img.Mutate(c =>
        {
            foreach (var r in rects)
            {
                c.Fill(Black, new SixLabors.ImageSharp.Drawing.RectangularPolygon(r.X, r.Y, r.Width, r.Height));
            }
        });
        return img;
    }

    /// <summary>White canvas with evenly spaced horizontal black bars (text-line proxy for deskew).</summary>
    public static Image<Rgba32> HorizontalBars(int size, int barThickness = 10, int gap = 30)
    {
        var img = new Image<Rgba32>(size, size, White);
        img.Mutate(c =>
        {
            for (int y = gap; y < size - gap; y += gap + barThickness)
            {
                c.Fill(Black, new SixLabors.ImageSharp.Drawing.RectangularPolygon(
                    size * 0.1f, y, size * 0.8f, barThickness));
            }
        });
        return img;
    }

    /// <summary>Rotates an image by the given degrees over a white background (no black corners).</summary>
    public static Image<Rgba32> RotatedOnWhite(Image<Rgba32> source, float degrees)
    {
        return source.Clone(c => c.Rotate(degrees).BackgroundColor(White));
    }
}
