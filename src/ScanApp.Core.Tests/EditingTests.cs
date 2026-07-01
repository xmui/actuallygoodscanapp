using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ScanApp.Core.Tests;

public class EditingTests
{
    private static int CountFarFrom(Image<Rgba32> img, byte target, int tolerance)
    {
        int n = 0;
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (Math.Abs(row[x].R - target) > tolerance) n++;
                }
            }
        });
        return n;
    }

    [Fact]
    public void DustRemoval_RemovesSpecksAndThinLines()
    {
        const byte bg = 128;
        using var img = new Image<Rgba32>(200, 200, new Rgba32(bg, bg, bg, 255));
        var rng = new Random(3);
        img.ProcessPixelRows(accessor =>
        {
            // Scattered dust specks.
            for (int i = 0; i < 120; i++)
            {
                int x = rng.Next(3, 197), y = rng.Next(3, 197);
                byte v = rng.Next(2) == 0 ? (byte)0 : (byte)255;
                accessor.GetRowSpan(y)[x] = new Rgba32(v, v, v, 255);
            }
            // A thin hair-like line.
            for (int x = 10; x < 190; x++)
            {
                accessor.GetRowSpan(100)[x] = new Rgba32(0, 0, 0, 255);
            }
        });

        int before = CountFarFrom(img, bg, 20);
        DustRemovalProcessor.Remove(img, strength: 70);
        int after = CountFarFrom(img, bg, 20);

        Assert.True(before > 200, $"expected plenty of defect pixels, got {before}");
        Assert.True(after < before / 5, $"dust removal should clear most defects: {before} -> {after}");
    }

    [Fact]
    public void Adjustments_BrightnessRaisesMean()
    {
        using var img = new Image<Rgba32>(64, 64, new Rgba32(100, 100, 100, 255));
        Adjustments.Apply(img, brightness: 40, contrast: 0);
        img.ProcessPixelRows(accessor =>
        {
            var p = accessor.GetRowSpan(0)[0];
            Assert.True(p.R > 110, $"brightness should raise value, got {p.R}");
        });
    }

    [Fact]
    public void AutoLevels_ExpandsLowContrastRange()
    {
        using var img = new Image<Rgba32>(256, 32, new Rgba32(0, 0, 0, 255));
        // Fill R with a narrow band 100..150 across the width.
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte v = (byte)(100 + (x % 51));
                    row[x] = new Rgba32(v, v, v, 255);
                }
            }
        });

        Adjustments.AutoLevels(img);

        byte min = 255, max = 0;
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].R < min) min = row[x].R;
                    if (row[x].R > max) max = row[x].R;
                }
            }
        });

        Assert.True(min <= 3, $"min should stretch to ~0, got {min}");
        Assert.True(max >= 252, $"max should stretch to ~255, got {max}");
    }

    [Fact]
    public void CropNormalized_ProducesExpectedRegion()
    {
        using var img = new Image<Rgba32>(400, 200, TestImages.White);
        using var cropped = PageProcessor.CropNormalized(img, new NormalizedRect(0.25, 0.5, 0.5, 0.5));
        Assert.Equal(200, cropped.Width);
        Assert.Equal(100, cropped.Height);
    }

    [Theory]
    [InlineData(7.0)]
    [InlineData(-12.0)]
    public void RotateKeepSize_PreservesDimensions(double degrees)
    {
        using var img = TestImages.WhiteWith(500, 300, new Rectangle(100, 80, 300, 140));
        using var rotated = RotationOps.RotateKeepSize(img, degrees);
        Assert.Equal(500, rotated.Width);
        Assert.Equal(300, rotated.Height);
    }

    [Fact]
    public void RotateKeepSize_TinyAngleIsNoOp()
    {
        using var img = TestImages.WhiteWith(120, 90, new Rectangle(10, 10, 40, 30));
        using var rotated = RotationOps.RotateKeepSize(img, 0.01);
        Assert.Equal(120, rotated.Width);
        Assert.Equal(90, rotated.Height);
    }

    [Fact]
    public void NormalizedRect_FullReturnsClone()
    {
        using var img = new Image<Rgba32>(120, 80, TestImages.White);
        using var clone = PageProcessor.CropNormalized(img, NormalizedRect.Full);
        Assert.Equal(120, clone.Width);
        Assert.Equal(80, clone.Height);
    }
}
