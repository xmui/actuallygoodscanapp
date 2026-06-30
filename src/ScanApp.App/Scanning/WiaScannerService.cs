using System.Runtime.InteropServices;
using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanApp.App.Scanning;

/// <summary>
/// WIA backend (fallback) using late-bound COM so it needs no extra package. Useful for the
/// Epson FF-680W, which exposes WIA via Epson Scan 2. WIA rarely auto-crops/deskews, so pages are
/// reported with no driver processing and the software pipeline does the cropping/straightening.
///
/// Windows-only; all entry points degrade gracefully (empty device list) on any failure.
/// </summary>
public sealed class WiaScannerService : IScannerService
{
    private const string WiaFormatBmp = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";

    // WIA property ids.
    private const int CurrentIntent = 6146;     // 1 = color, 2 = grayscale
    private const int HorizontalResolution = 6147;
    private const int VerticalResolution = 6148;
    private const int DocumentHandlingSelect = 3088; // 1 = feeder, 2 = flatbed
    private const int DocumentHandlingStatus = 3087;
    private const uint PaperEmpty = 0x80210003;

    public string Backend => "WIA";

    private static dynamic? CreateDeviceManager()
    {
        var t = Type.GetTypeFromProgID("WIA.DeviceManager");
        return t is null ? null : Activator.CreateInstance(t);
    }

    public IReadOnlyList<ScannerDevice> GetDevices()
    {
        var list = new List<ScannerDevice>();
        try
        {
            dynamic? dm = CreateDeviceManager();
            if (dm is null)
            {
                return list;
            }

            foreach (dynamic info in dm.DeviceInfos)
            {
                try
                {
                    // Type 1 == scanner.
                    if ((int)info.Type != 1)
                    {
                        continue;
                    }

                    string id = info.DeviceID;
                    string name = GetProp(info.Properties, "Name") as string ?? id;
                    list.Add(new ScannerDevice { Id = id, Name = name, Backend = Backend });
                }
                catch
                {
                    // skip a device we can't read
                }
            }
        }
        catch
        {
            // no WIA available
        }

        return list;
    }

    public ScannerCapabilities QueryCapabilities(ScannerDevice device) => new()
    {
        SupportsFlatbed = true,
        SupportsFeeder = true,
        SupportsDuplex = false,
        SupportsAutoCrop = false,
        SupportsDeskew = false
    };

    public Task ScanAsync(ScannerDevice device, ScanProfile profile, Action<RawScan> onPage, CancellationToken cancellationToken) =>
        Task.Run(() => ScanCore(device, profile, onPage, cancellationToken), cancellationToken);

    private void ScanCore(ScannerDevice device, ScanProfile profile, Action<RawScan> onPage, CancellationToken cancellationToken)
    {
        dynamic? dm = CreateDeviceManager();
        if (dm is null)
        {
            throw new InvalidOperationException("WIA is not available on this system.");
        }

        dynamic? connected = null;
        foreach (dynamic info in dm.DeviceInfos)
        {
            if ((string)info.DeviceID == device.Id)
            {
                connected = info.Connect();
                break;
            }
        }

        if (connected is null)
        {
            throw new InvalidOperationException($"Scanner '{device.Name}' not found.");
        }

        dynamic item = connected.Items[1];
        TrySetProp(item.Properties, HorizontalResolution, profile.Dpi);
        TrySetProp(item.Properties, VerticalResolution, profile.Dpi);
        TrySetProp(item.Properties, CurrentIntent, profile.Color ? 1 : 2);

        if (profile.Mode == ScanMode.Sheetfed)
        {
            TrySetProp(connected.Properties, DocumentHandlingSelect, 1); // feeder
            ScanFeeder(connected, item, profile, onPage, cancellationToken);
        }
        else
        {
            TrySetProp(connected.Properties, DocumentHandlingSelect, 2); // flatbed
            cancellationToken.ThrowIfCancellationRequested();
            TransferOne(item, profile, onPage);
        }
    }

    private void ScanFeeder(dynamic device, dynamic item, ScanProfile profile, Action<RawScan> onPage, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                TransferOne(item, profile, onPage);
            }
            catch (COMException ex) when ((uint)ex.ErrorCode == PaperEmpty)
            {
                break; // feeder empty -> done
            }

            // Stop if the feeder reports no more pages ready.
            try
            {
                int status = Convert.ToInt32(GetProp(device.Properties, DocumentHandlingStatus) ?? 0);
                const int feedReady = 0x01;
                if ((status & feedReady) == 0)
                {
                    break;
                }
            }
            catch
            {
                // if status is unreadable, rely on the PaperEmpty exception to terminate
            }
        }
    }

    private static void TransferOne(dynamic item, ScanProfile profile, Action<RawScan> onPage)
    {
        dynamic imageFile = item.Transfer(WiaFormatBmp);
        byte[] bytes = (byte[])imageFile.FileData.BinaryData;
        var image = Image.Load<Rgba32>(bytes);
        onPage(new RawScan { Image = image, Dpi = profile.Dpi, DriverDid = DriverCapabilities.None });
    }

    private static object? GetProp(dynamic properties, object key)
    {
        try
        {
            foreach (dynamic p in properties)
            {
                bool match = key is int id
                    ? (int)p.PropertyID == id
                    : string.Equals((string)p.Name, key.ToString(), StringComparison.OrdinalIgnoreCase);
                if (match)
                {
                    return p.Value;
                }
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private static void TrySetProp(dynamic properties, int propertyId, object value)
    {
        try
        {
            foreach (dynamic p in properties)
            {
                if ((int)p.PropertyID == propertyId)
                {
                    p.Value = value;
                    return;
                }
            }
        }
        catch
        {
            // property not settable on this device; ignore
        }
    }

    public void Dispose()
    {
    }
}
