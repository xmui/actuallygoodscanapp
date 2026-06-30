namespace ScanApp.Core.Models;

/// <summary>
/// The set of options that drive a scan session and the post-processing pipeline.
/// Sensible defaults differ per <see cref="ScanMode"/>; see <see cref="ForMode"/>.
/// </summary>
public sealed class ScanProfile
{
    public ScanMode Mode { get; set; } = ScanMode.BulkFlatbed;

    /// <summary>Scan resolution in dots per inch requested from the driver.</summary>
    public int Dpi { get; set; } = 300;

    /// <summary>Scan in colour (true) or grayscale (false).</summary>
    public bool Color { get; set; } = true;

    /// <summary>True to feed/scan both sides (ADF duplex). Ignored on flatbed.</summary>
    public bool Duplex { get; set; }

    public CropMode CropMode { get; set; } = CropMode.AutoDetect;

    public FixedSizePreset FixedSize { get; set; } = FixedSizePreset.Letter;

    /// <summary>Run software auto-crop when the scanner driver does not crop itself.</summary>
    public bool SoftwareAutoCrop { get; set; } = true;

    /// <summary>Run software deskew (straighten) when the driver does not deskew itself.</summary>
    public bool SoftwareDeskew { get; set; } = true;

    /// <summary>Auto-rotate the page to upright orientation after deskew.</summary>
    public bool AutoRotate { get; set; } = true;

    /// <summary>
    /// Bulk-flatbed only: detect multiple separate photos/items in a single glass scan and
    /// split each into its own page (FastFoto style).
    /// </summary>
    public bool SplitMultiplePhotos { get; set; }

    /// <summary>Prefer the scanner driver's built-in auto-crop/deskew when it advertises support.</summary>
    public bool PreferDriverAutoFeatures { get; set; } = true;

    public ScanProfile Clone() => (ScanProfile)MemberwiseClone();

    /// <summary>Returns a profile pre-tuned for the given mode.</summary>
    public static ScanProfile ForMode(ScanMode mode) => mode switch
    {
        ScanMode.Sheetfed => new ScanProfile
        {
            Mode = ScanMode.Sheetfed,
            Dpi = 300,
            Color = true,
            Duplex = false,
            CropMode = CropMode.AutoDetect,
            SplitMultiplePhotos = false
        },
        _ => new ScanProfile
        {
            Mode = ScanMode.BulkFlatbed,
            Dpi = 300,
            Color = true,
            Duplex = false,
            CropMode = CropMode.AutoDetect,
            SplitMultiplePhotos = false
        }
    };
}
