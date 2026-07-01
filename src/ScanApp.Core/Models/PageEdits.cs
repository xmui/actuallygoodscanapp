namespace ScanApp.Core.Models;

/// <summary>
/// The full reversible edit state of a page. Serialized into projects, snapshotted for undo/redo,
/// and copied by "apply to all". Kept as a plain record so it round-trips cleanly through JSON.
/// </summary>
public sealed record PageEdits
{
    public NormalizedRect Crop { get; init; } = NormalizedRect.Full;
    public bool CropActive { get; init; }
    public double RotationDegrees { get; init; }
    public int Brightness { get; init; }
    public int Contrast { get; init; }
    public bool RemoveDust { get; init; }
    public bool AutoLevel { get; init; }

    /// <summary>Sensible defaults for a freshly scanned/imported page (auto-levels + crop box on).</summary>
    public static PageEdits Defaults => new() { AutoLevel = true, CropActive = true };
}
