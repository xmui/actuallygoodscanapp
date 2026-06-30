using ScanApp.Core.Models;

namespace ScanApp.App.Scanning;

/// <summary>What a backend can do, so the UI can enable the driver's own auto-features when present.</summary>
public sealed class ScannerCapabilities
{
    public bool SupportsFlatbed { get; init; } = true;
    public bool SupportsFeeder { get; init; }
    public bool SupportsDuplex { get; init; }

    /// <summary>Driver can auto-crop / auto border-detect.</summary>
    public bool SupportsAutoCrop { get; init; }

    /// <summary>Driver can deskew / auto-rotate.</summary>
    public bool SupportsDeskew { get; init; }
}

/// <summary>
/// Backend-agnostic scanning interface. Implementations: TWAIN (primary), WIA (fallback), Mock
/// (no hardware). Pages are delivered one at a time via <paramref name="onPage"/> so the UI can
/// show live previews as each page arrives (essential for ADF batches).
/// </summary>
public interface IScannerService : IDisposable
{
    /// <summary>Human-readable backend name (e.g. "TWAIN", "WIA", "Demo").</summary>
    string Backend { get; }

    /// <summary>Enumerates available devices. Should never throw; returns empty on failure.</summary>
    IReadOnlyList<ScannerDevice> GetDevices();

    ScannerCapabilities QueryCapabilities(ScannerDevice device);

    /// <summary>
    /// Scans using the device and profile, invoking <paramref name="onPage"/> for each raw page as
    /// it is acquired. For flatbed this yields a single page; for the ADF it streams every fed sheet.
    /// </summary>
    Task ScanAsync(
        ScannerDevice device,
        ScanProfile profile,
        Action<RawScan> onPage,
        CancellationToken cancellationToken);
}
