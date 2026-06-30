using ScanApp.Core.Models;

namespace ScanApp.App.Scanning;

/// <summary>
/// Aggregates every available scanner backend (TWAIN, WIA, Demo) behind one interface and routes
/// each operation to the backend that owns the chosen device. TWAIN is preferred; WIA is a fallback;
/// the Demo backend is always present so the app is usable with no hardware. Backend construction is
/// guarded so a missing/broken driver never prevents the app from starting.
/// </summary>
public sealed class ScannerHub : IDisposable
{
    private readonly List<IScannerService> _backends = new();
    private readonly MockScannerService _mock = new();

    public ScannerHub()
    {
        // Order matters: real hardware first, demo last.
        TryAdd(() => new TwainScannerService());
        TryAdd(() => new WiaScannerService());
        _backends.Add(_mock);
    }

    private void TryAdd(Func<IScannerService> factory)
    {
        try
        {
            _backends.Add(factory());
        }
        catch
        {
            // backend unavailable on this machine; skip it
        }
    }

    /// <summary>All devices across all backends. Real devices first, demo devices last.</summary>
    public IReadOnlyList<ScannerDevice> GetDevices()
    {
        var devices = new List<ScannerDevice>();
        foreach (var backend in _backends)
        {
            try
            {
                devices.AddRange(backend.GetDevices());
            }
            catch
            {
                // ignore a misbehaving backend
            }
        }
        return devices;
    }

    public ScannerCapabilities QueryCapabilities(ScannerDevice device) =>
        Resolve(device).QueryCapabilities(device);

    public Task ScanAsync(ScannerDevice device, ScanProfile profile, Action<RawScan> onPage, CancellationToken cancellationToken) =>
        Resolve(device).ScanAsync(device, profile, onPage, cancellationToken);

    private IScannerService Resolve(ScannerDevice device) =>
        _backends.FirstOrDefault(b => b.Backend == device.Backend) ?? _mock;

    public void Dispose()
    {
        foreach (var backend in _backends)
        {
            try { backend.Dispose(); } catch { /* ignore */ }
        }
    }
}
