using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanApp.Services.Editing;

/// <summary>
/// The single, canonical edit-render chain: pristine original + <see cref="PageEdits"/> → working
/// image. Every consumer (live preview, export, apply-to-all) goes through this so the order of
/// operations can never drift between codepaths.
/// </summary>
public static class EditRenderer
{
    /// <summary>Renders the working image (crop NOT applied — crop is an export-time concern).</summary>
    public static Image<Rgba32> Render(Image<Rgba32> original, PageEdits edits)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edits);

        var img = RotationOps.RotateKeepSize(original, edits.RotationDegrees);
        if (edits.RemoveDust)
        {
            DustRemovalProcessor.Remove(img, 60);
        }
        if (edits.AutoLevel)
        {
            Adjustments.AutoLevels(img);
        }
        Adjustments.Apply(img, edits.Brightness, edits.Contrast);
        if (edits.DocumentCleanup)
        {
            DocumentCleanup.Apply(img, edits.BlackAndWhite);
        }
        return img;
    }

    /// <summary>Renders the final export image: the working image with the crop applied.</summary>
    public static Image<Rgba32> RenderForExport(Image<Rgba32> original, PageEdits edits)
    {
        using var working = Render(original, edits);
        var crop = edits.CropActive ? edits.Crop : NormalizedRect.Full;
        return PageProcessor.CropNormalized(working, crop);
    }
}
