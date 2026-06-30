using System.Collections.Concurrent;
using System.Reflection;
using NTwain;
using NTwain.Data;
using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanApp.App.Scanning;

/// <summary>
/// TWAIN backend (primary path) built on NTwain. Both target scanners — Epson FF-680W and Plustek
/// OpticBook A300 Plus — ship TWAIN drivers, so this is the common, most capable route.
///
/// NOTE: This file targets the NTwain 3.7 API and must be built/run on Windows against the real
/// drivers. It uses NTwain's internal message loop (Enable with IntPtr.Zero) and the native transfer
/// mechanism. The whole class is defensive: any failure surfaces as "no devices" so the app falls
/// back to WIA / Demo rather than crashing.
/// </summary>
public sealed class TwainScannerService : IScannerService
{
    private readonly TwainSession _session;
    private bool _opened;

    public TwainScannerService()
    {
        var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
        _session = new TwainSession(appId);
    }

    public string Backend => "TWAIN";

    private void EnsureOpen()
    {
        if (_opened)
        {
            return;
        }

        // NTwain runs its own internal message pump when the session is opened with no parent window.
        _session.Open();
        _opened = true;
    }

    public IReadOnlyList<ScannerDevice> GetDevices()
    {
        try
        {
            EnsureOpen();
            return _session.GetSources()
                .Select(s => new ScannerDevice { Id = s.Name, Name = s.Name, Backend = Backend })
                .ToList();
        }
        catch
        {
            return Array.Empty<ScannerDevice>();
        }
    }

    public ScannerCapabilities QueryCapabilities(ScannerDevice device)
    {
        try
        {
            EnsureOpen();
            var src = FindSource(device);
            if (src is null)
            {
                return new ScannerCapabilities();
            }

            bool wasOpen = src.IsOpen;
            if (!wasOpen)
            {
                src.Open();
            }

            var caps = new ScannerCapabilities
            {
                SupportsFlatbed = true,
                SupportsFeeder = IsSupported(src.Capabilities.CapFeederEnabled),
                SupportsDuplex = IsSupported(src.Capabilities.CapDuplexEnabled),
                SupportsAutoCrop = IsSupported(src.Capabilities.ICapAutomaticBorderDetection),
                SupportsDeskew = IsSupported(src.Capabilities.ICapAutomaticDeskew)
            };

            if (!wasOpen)
            {
                src.Close();
            }
            return caps;
        }
        catch
        {
            return new ScannerCapabilities();
        }
    }

    public Task ScanAsync(ScannerDevice device, ScanProfile profile, Action<RawScan> onPage, CancellationToken cancellationToken)
    {
        // The acquisition itself drives NTwain's pump; run it off the UI thread and await completion.
        return Task.Run(() => ScanCore(device, profile, onPage, cancellationToken), cancellationToken);
    }

    private void ScanCore(ScannerDevice device, ScanProfile profile, Action<RawScan> onPage, CancellationToken cancellationToken)
    {
        EnsureOpen();
        var src = FindSource(device) ?? throw new InvalidOperationException($"Scanner '{device.Name}' not found.");
        if (!src.IsOpen)
        {
            src.Open();
        }

        bool driverCrop = false;
        bool driverDeskew = false;
        ConfigureCapabilities(src, profile, ref driverCrop, ref driverDeskew);

        var completion = new TaskCompletionSource<bool>();
        var pending = new ConcurrentQueue<Exception>();

        void OnData(object? s, DataTransferredEventArgs e)
        {
            try
            {
                using var stream = e.GetNativeImageStream();
                if (stream is null)
                {
                    return;
                }

                var image = Image.Load<Rgba32>(stream);
                onPage(new RawScan
                {
                    Image = image,
                    Dpi = profile.Dpi,
                    DriverDid = new DriverCapabilities(driverCrop, driverDeskew)
                });
            }
            catch (Exception ex)
            {
                pending.Enqueue(ex);
            }
        }

        void OnDisabled(object? s, EventArgs e) => completion.TrySetResult(true);

        _session.DataTransferred += OnData;
        _session.SourceDisabled += OnDisabled;
        using var ctr = cancellationToken.Register(() =>
        {
            try { _session.ForceStepDown(3); } catch { /* best effort */ }
            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            // NoUI + IntPtr.Zero => silent acquisition through NTwain's own message loop.
            var rc = src.Enable(SourceEnableMode.NoUI, modal: false, IntPtr.Zero);
            if (rc != ReturnCode.Success)
            {
                throw new InvalidOperationException($"Scanner did not start (TWAIN code {rc}).");
            }

            completion.Task.GetAwaiter().GetResult();
        }
        finally
        {
            _session.DataTransferred -= OnData;
            _session.SourceDisabled -= OnDisabled;
        }

        if (pending.TryDequeue(out var first))
        {
            throw first;
        }
    }

    private void ConfigureCapabilities(DataSource src, ScanProfile profile, ref bool driverCrop, ref bool driverDeskew)
    {
        TrySet(() => src.Capabilities.ICapXferMech.SetValue(XferMech.Native));
        TrySet(() => src.Capabilities.ICapXResolution.SetValue(profile.Dpi));
        TrySet(() => src.Capabilities.ICapYResolution.SetValue(profile.Dpi));
        TrySet(() => src.Capabilities.ICapPixelType.SetValue(profile.Color ? PixelType.RGB : PixelType.Gray));

        // Hide the driver's own progress / transfer window so scanning stays fully in-app.
        TrySet(() => src.Capabilities.CapIndicators.SetValue(BoolType.False));

        bool feeder = profile.Mode == ScanMode.Sheetfed;
        TrySet(() => src.Capabilities.CapFeederEnabled.SetValue(feeder ? BoolType.True : BoolType.False));
        if (feeder)
        {
            TrySet(() => src.Capabilities.CapDuplexEnabled.SetValue(profile.Duplex ? BoolType.True : BoolType.False));
        }

        // Splitting multiple photos needs the whole platen, so never let the driver auto-crop here.
        bool wantDriverCrop = profile.PreferDriverAutoFeatures && !profile.SplitMultiplePhotos;
        if (!wantDriverCrop)
        {
            TrySet(() => src.Capabilities.ICapAutomaticBorderDetection.SetValue(BoolType.False));
        }

        if (profile.PreferDriverAutoFeatures)
        {
            if (wantDriverCrop && IsSupported(src.Capabilities.ICapAutomaticBorderDetection))
            {
                if (TrySet(() => src.Capabilities.ICapAutomaticBorderDetection.SetValue(BoolType.True)))
                {
                    driverCrop = true;
                }
            }
            if (IsSupported(src.Capabilities.ICapAutomaticDeskew))
            {
                if (TrySet(() => src.Capabilities.ICapAutomaticDeskew.SetValue(BoolType.True)))
                {
                    driverDeskew = true;
                }
            }
        }
    }

    private DataSource? FindSource(ScannerDevice device) =>
        _session.GetSources().FirstOrDefault(s => string.Equals(s.Name, device.Id, StringComparison.OrdinalIgnoreCase));

    private static bool IsSupported(object capability)
    {
        try
        {
            // NTwain capability wrappers expose IsSupported; access defensively across versions.
            var prop = capability.GetType().GetProperty("IsSupported");
            return prop?.GetValue(capability) is true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySet(Action set)
    {
        try
        {
            set();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_opened)
            {
                if (_session.State > 3)
                {
                    _session.ForceStepDown(2);
                }
                _session.Close();
            }
        }
        catch
        {
            // ignore teardown errors
        }
    }
}
