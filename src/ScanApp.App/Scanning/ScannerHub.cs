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

    /// <summary>
    /// Real (non-demo) scanners, deduplicated so the same physical device is not listed once per
    /// backend. When a scanner is exposed by both TWAIN and WIA, the TWAIN entry wins.
    /// </summary>
    public IReadOnlyList<ScannerDevice> GetRealDevices()
    {
        var all = new List<ScannerDevice>();
        foreach (var backend in _backends)
        {
            if (ReferenceEquals(backend, _mock))
            {
                continue;
            }
            try
            {
                all.AddRange(backend.GetDevices());
            }
            catch
            {
                // ignore a misbehaving backend
            }
        }

        // Dedupe by normalized name, preferring TWAIN over WIA.
        var byKey = new Dictionary<string, ScannerDevice>();
        foreach (var d in all.OrderBy(d => d.Backend == "TWAIN" ? 0 : 1))
        {
            var key = NormalizeName(d.Name);
            if (!byKey.ContainsKey(key))
            {
                byKey[key] = d;
            }
        }
        return byKey.Values.ToList();
    }

    /// <summary>The demo devices, shown only when no real scanner is connected.</summary>
    public IReadOnlyList<ScannerDevice> GetDemoDevices() => _mock.GetDevices();

    private static string NormalizeName(string name)
    {
        // Lowercase, strip non-alphanumerics and backend/driver noise words, so "EPSON FF-680W"
        // (TWAIN) and "WIA-EPSON FF-680W" match.
        var cleaned = new string(name.ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        foreach (var noise in new[] { "wia", "twain", "scanner", "driver" })
        {
            cleaned = cleaned.Replace(noise, "");
        }
        return cleaned;
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
