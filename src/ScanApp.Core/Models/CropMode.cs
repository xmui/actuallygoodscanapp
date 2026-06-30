namespace ScanApp.Core.Models;

/// <summary>
/// How the crop rectangle for a scanned page is determined.
/// </summary>
public enum CropMode
{
    /// <summary>Detect the content bounding box automatically (default).</summary>
    AutoDetect,

    /// <summary>Crop to a fixed physical size (A4, Letter, 4x6, ...).</summary>
    FixedSize,

    /// <summary>Use a user-supplied crop rectangle, or none at all.</summary>
    Manual
}

/// <summary>
/// Common fixed paper / photo sizes for <see cref="CropMode.FixedSize"/>, expressed in inches.
/// </summary>
public enum FixedSizePreset
{
    A4,
    Letter,
    Legal,
    Photo4x6,
    Photo5x7
}

public static class FixedSizePresetExtensions
{
    /// <summary>Returns (width, height) of the preset in inches, portrait orientation.</summary>
    public static (double WidthInches, double HeightInches) ToInches(this FixedSizePreset preset) => preset switch
    {
        FixedSizePreset.A4 => (8.27, 11.69),
        FixedSizePreset.Letter => (8.5, 11.0),
        FixedSizePreset.Legal => (8.5, 14.0),
        FixedSizePreset.Photo4x6 => (4.0, 6.0),
        FixedSizePreset.Photo5x7 => (5.0, 7.0),
        _ => (8.5, 11.0)
    };
}
