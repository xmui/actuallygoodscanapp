using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
        var twain = DevicesFrom("TWAIN");
        var wia = DevicesFrom("WIA");

        // WIA enumerates *connected* devices; TWAIN advertises *installed drivers* (which may be for an
        // unplugged scanner). So when WIA is available, treat its set as ground truth for "connected":
        // keep WIA devices, and keep a TWAIN source only if a connected WIA twin exists (preferring the
        // TWAIN entry for its richer transfer). Best-effort — documented caveat.
        bool haveWia = wia.Count > 0;
        var wiaKeys = wia.Select(d => NormalizeName(d.Name)).ToList();

        bool Connected(ScannerDevice d)
        {
            if (!haveWia)
            {
                return true; // no way to tell; show everything
            }
            if (d.Backend == "WIA")
            {
                return true;
            }
            var k = NormalizeName(d.Name);
            return wiaKeys.Any(wk => FuzzyMatch(k, wk));
        }

        var candidates = twain.Concat(wia).Where(Connected)
            .OrderBy(d => d.Backend == "TWAIN" ? 0 : 1) // prefer TWAIN when deduping
            .ToList();

        // Dedupe: drop a device that fuzzily matches one already kept.
        var kept = new List<ScannerDevice>();
        foreach (var d in candidates)
        {
            var k = NormalizeName(d.Name);
            if (!kept.Any(x => FuzzyMatch(NormalizeName(x.Name), k)))
            {
                kept.Add(d);
            }
        }
        return kept;
    }

    private List<ScannerDevice> DevicesFrom(string backendName)
    {
        var backend = _backends.FirstOrDefault(b => b.Backend == backendName);
        if (backend is null)
        {
            return new List<ScannerDevice>();
        }
        try
        {
            return backend.GetDevices().ToList();
        }
        catch
        {
            return new List<ScannerDevice>();
        }
    }

    private static bool FuzzyMatch(string a, string b)
    {
        if (a.Length < 4 || b.Length < 4)
        {
            return a == b;
        }
        return a == b || a.Contains(b) || b.Contains(a);
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

    public Task ScanAsync(ScannerDevice device, ScanProfile profile, Action<RawScan> onPage, Action<Image<Rgba32>>? onPreview, CancellationToken cancellationToken) =>
        Resolve(device).ScanAsync(device, profile, onPage, onPreview, cancellationToken);

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
