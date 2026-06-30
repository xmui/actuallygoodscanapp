using ScanApp.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanApp.App.Scanning;

/// <summary>
/// One image as it comes off the scanner, before the software post-processing pipeline runs.
/// Carries what the driver already did so the pipeline can skip redundant work (hybrid auto-crop).
/// </summary>
public sealed class RawScan
{
    public required Image<Rgba32> Image { get; init; }
    public int Dpi { get; init; } = 300;
    public DriverCapabilities DriverDid { get; init; } = DriverCapabilities.None;
}
