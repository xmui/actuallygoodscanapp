using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using Xunit;

namespace ScanApp.Core.Tests;

public class ImagingTests
{
    [Fact]
    public void AutoCrop_ReducesToContentBoundingBox()
    {
        var content = new Rectangle(200, 150, 400, 300);
        using var img = TestImages.WhiteWith(1000, 800, content);

        using var cropped = AutoCropProcessor.AutoCrop(img, marginFraction: 0.0);

        // Cropped result should be far smaller than the source and close to the content size.
        Assert.True(cropped.Width < img.Width);
        Assert.True(cropped.Height < img.Height);
        Assert.InRange(cropped.Width, content.Width - 30, content.Width + 30);
        Assert.InRange(cropped.Height, content.Height - 30, content.Height + 30);
    }

    [Fact]
    public void AutoCrop_BlankPage_ReturnsOriginalSize()
    {
        using var blank = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(500, 400, TestImages.White);
        using var cropped = AutoCropProcessor.AutoCrop(blank);
        Assert.Equal(blank.Width, cropped.Width);
        Assert.Equal(blank.Height, cropped.Height);
    }

    [Fact]
    public void CropToFixedSize_ProducesRequestedPixelDimensions()
    {
        using var img = TestImages.WhiteWith(2000, 2600, new Rectangle(400, 500, 800, 1000));
        using var cropped = AutoCropProcessor.CropToFixedSize(img, FixedSizePreset.Photo4x6, dpi: 100);

        // 4x6 inches at 100 dpi -> 400x600 px.
        Assert.Equal(400, cropped.Width);
        Assert.Equal(600, cropped.Height);
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(-8f)]
    public void Deskew_StraightensSkewedContent(float appliedSkew)
    {
        using var straight = TestImages.HorizontalBars(700);
        using var skewed = TestImages.RotatedOnWhite(straight, appliedSkew);

        double residualBefore = Math.Abs(DeskewProcessor.EstimateSkewDegrees(skewed));
        using var deskewed = DeskewProcessor.Deskew(skewed);
        double residualAfter = Math.Abs(DeskewProcessor.EstimateSkewDegrees(deskewed));

        // The skew should be substantially reduced and the output close to straight.
        Assert.True(residualBefore > 3.0, $"expected detectable skew, got {residualBefore:F2}");
        Assert.True(residualAfter < 1.5, $"residual skew too high after deskew: {residualAfter:F2}");
    }

    [Fact]
    public void MultiPhotoSplitter_FindsTwoSeparateItems()
    {
        var rects = new[]
        {
            new Rectangle(100, 100, 300, 250),
            new Rectangle(700, 600, 350, 280)
        };
        using var img = TestImages.WhiteWithMany(1300, 1000, rects);

        var boxes = MultiPhotoSplitter.DetectItemRects(img);
        Assert.Equal(2, boxes.Count);

        var images = MultiPhotoSplitter.Split(img);
        try
        {
            Assert.Equal(2, images.Count);
        }
        finally
        {
            foreach (var i in images) i.Dispose();
        }
    }

    [Fact]
    public void MultiPhotoSplitter_SingleItem_ReturnsOneCrop()
    {
        using var img = TestImages.WhiteWith(1000, 800, new Rectangle(200, 150, 400, 300));
        var images = MultiPhotoSplitter.Split(img);
        try
        {
            Assert.Single(images);
        }
        finally
        {
            foreach (var i in images) i.Dispose();
        }
    }

    [Fact]
    public void PageProcessor_SplitMode_ProducesPagePerPhoto()
    {
        var rects = new[]
        {
            new Rectangle(100, 100, 300, 250),
            new Rectangle(700, 550, 350, 280)
        };
        var img = TestImages.WhiteWithMany(1300, 1000, rects);
        var profile = ScanProfile.ForMode(ScanMode.BulkFlatbed);
        profile.SplitMultiplePhotos = true;

        var pages = PageProcessor.Process(img, profile);
        try
        {
            Assert.Equal(2, pages.Count);
        }
        finally
        {
            foreach (var p in pages) p.Dispose();
        }
    }

    [Fact]
    public void Rotate90_SwapsDimensions()
    {
        var img = TestImages.WhiteWith(600, 400, new Rectangle(100, 100, 200, 100));
        using var page = new ScannedPage(img, 300);
        int w = page.Width, h = page.Height;

        PageProcessor.Rotate90(page, 1);

        Assert.Equal(h, page.Width);
        Assert.Equal(w, page.Height);
    }
}
