using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.Core.Imaging;

/// <summary>What the scanner driver already did, so the software pipeline doesn't redo it.</summary>
public readonly record struct DriverCapabilities(bool Cropped, bool Deskewed)
{
    public static readonly DriverCapabilities None = new(false, false);
}

/// <summary>
/// Turns a raw scanned image into one or more finished <see cref="ScannedPage"/>s according to a
/// <see cref="ScanProfile"/>. This is the hybrid auto-crop/deskew orchestration point: it only runs
/// the software crop/deskew steps the driver did not already perform.
/// </summary>
public static class PageProcessor
{
    /// <summary>
    /// Processes one raw scan. Returns one page normally, or multiple pages when bulk-flatbed photo
    /// splitting is enabled and several items are detected. The raw <paramref name="raw"/> image is
    /// consumed (disposed); callers should not use it afterwards.
    /// </summary>
    public static List<ScannedPage> Process(Image<Rgba32> raw, ScanProfile profile, DriverCapabilities driver = default)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(profile);

        var pages = new List<ScannedPage>();
        bool deskewNeeded = profile.SoftwareDeskew && !driver.Deskewed;

        // Photo splitting needs the whole platen. The scanner driver's auto-crop is disabled for this
        // mode (see TwainScannerService), so run the splitter regardless of the driver flag.
        if (profile.Mode == ScanMode.BulkFlatbed && profile.SplitMultiplePhotos)
        {
            var crops = MultiPhotoSplitter.Split(raw);
            foreach (var crop in crops)
            {
                pages.Add(MakePage(crop, profile, deskewNeeded));
            }
            raw.Dispose();
            return pages;
        }

        Image<Rgba32> cropped = CropForProfile(raw, profile, driver);
        raw.Dispose();
        pages.Add(MakePage(cropped, profile, deskewNeeded));
        return pages;
    }

    private static ScannedPage MakePage(Image<Rgba32> cropped, ScanProfile profile, bool deskewNeeded)
    {
        if (deskewNeeded)
        {
            var deskewed = DeskewProcessor.Deskew(cropped);
            if (!ReferenceEquals(deskewed, cropped))
            {
                cropped.Dispose();
                cropped = deskewed;
            }
        }

        return new ScannedPage(cropped, profile.Dpi);
    }

    private static Image<Rgba32> CropForProfile(Image<Rgba32> raw, ScanProfile profile, DriverCapabilities driver)
    {
        if (driver.Cropped)
        {
            return raw.Clone();
        }

        return profile.CropMode switch
        {
            CropMode.AutoDetect when profile.SoftwareAutoCrop => AutoCropProcessor.AutoCrop(raw),
            CropMode.FixedSize => AutoCropProcessor.CropToFixedSize(raw, profile.FixedSize, profile.Dpi),
            _ => raw.Clone()
        };
    }

    /// <summary>Rotates a page in place by a multiple of 90 degrees (positive = clockwise).</summary>
    public static void Rotate90(ScannedPage page, int quarterTurns)
    {
        ArgumentNullException.ThrowIfNull(page);
        int turns = ((quarterTurns % 4) + 4) % 4;
        if (turns == 0)
        {
            return;
        }

        float degrees = turns * 90f;
        var rotated = page.Image.Clone(c => c.Rotate(degrees));
        page.ReplaceImage(rotated);
    }

    /// <summary>Applies a user-supplied crop rectangle (page coordinates), clamped to bounds.</summary>
    public static void ApplyManualCrop(ScannedPage page, Rectangle rect)
    {
        ArgumentNullException.ThrowIfNull(page);
        int x = Math.Clamp(rect.X, 0, page.Width - 1);
        int y = Math.Clamp(rect.Y, 0, page.Height - 1);
        int w = Math.Clamp(rect.Width, 1, page.Width - x);
        int h = Math.Clamp(rect.Height, 1, page.Height - y);
        var cropped = page.Image.Clone(c => c.Crop(new Rectangle(x, y, w, h)));
        page.ReplaceImage(cropped);
    }
}
