using ScanApp.Core.Models;

namespace ScanApp.Services.Presets;

/// <summary>
/// A named, user-saved scan configuration (like VueScan/NAPS2 profiles): everything needed to set
/// the app up for a recurring job in one click.
/// </summary>
public sealed record ScanPreset
{
    public required string Name { get; init; }
    public ScanMode Mode { get; init; } = ScanMode.BulkFlatbed;
    public int Dpi { get; init; } = 300;
    public bool Color { get; init; } = true;
    public bool Duplex { get; init; }
    public bool SplitMultiplePhotos { get; init; }
    public bool SoftwareDeskew { get; init; } = true;
    public CropMode CropMode { get; init; } = CropMode.AutoDetect;
    public FixedSizePreset FixedSize { get; init; } = FixedSizePreset.Letter;
    public OutputFormat Format { get; init; } = OutputFormat.Jpeg;

    /// <summary>Applies this preset onto a live profile (mutating the profile in place).</summary>
    public void ApplyTo(ScanProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Mode = Mode;
        profile.Dpi = Dpi;
        profile.Color = Color;
        profile.Duplex = Duplex;
        profile.SplitMultiplePhotos = SplitMultiplePhotos;
        profile.SoftwareDeskew = SoftwareDeskew;
        profile.CropMode = CropMode;
        profile.FixedSize = FixedSize;
    }

    /// <summary>Captures the current configuration as a preset.</summary>
    public static ScanPreset From(string name, ScanProfile profile, OutputFormat format) => new()
    {
        Name = name,
        Mode = profile.Mode,
        Dpi = profile.Dpi,
        Color = profile.Color,
        Duplex = profile.Duplex,
        SplitMultiplePhotos = profile.SplitMultiplePhotos,
        SoftwareDeskew = profile.SoftwareDeskew,
        CropMode = profile.CropMode,
        FixedSize = profile.FixedSize,
        Format = format
    };
}
