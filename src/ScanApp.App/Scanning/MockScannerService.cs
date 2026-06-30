using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.App.Scanning;

/// <summary>
/// A hardware-free scanner that synthesises realistic-looking scans (slightly skewed, oversized
/// margins, sometimes multiple photos on the glass). Lets the whole app be exercised end-to-end —
/// preview, auto-crop, deskew, split, save — with no scanner attached, and is the safe fallback
/// when no TWAIN/WIA device is found.
/// </summary>
public sealed class MockScannerService : IScannerService
{
    private readonly Random _rng = new();

    public string Backend => "Demo";

    public IReadOnlyList<ScannerDevice> GetDevices() => new[]
    {
        new ScannerDevice { Id = "demo-flatbed", Name = "Demo Flatbed (no hardware)", Backend = Backend },
        new ScannerDevice { Id = "demo-adf", Name = "Demo Sheetfed (no hardware)", Backend = Backend }
    };

    public ScannerCapabilities QueryCapabilities(ScannerDevice device) => new()
    {
        SupportsFlatbed = true,
        SupportsFeeder = true,
        SupportsDuplex = true,
        SupportsAutoCrop = false, // force the software pipeline so the demo shows it off
        SupportsDeskew = false
    };

    public async Task ScanAsync(ScannerDevice device, ScanProfile profile, Action<RawScan> onPage, CancellationToken cancellationToken)
    {
        // Sheetfed streams several pages; flatbed yields a single page per press.
        int pageCount = profile.Mode == ScanMode.Sheetfed ? 3 : 1;
        for (int i = 0; i < pageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(profile.Mode == ScanMode.Sheetfed ? 450 : 250, cancellationToken).ConfigureAwait(false);

            bool multi = profile.Mode == ScanMode.BulkFlatbed && profile.SplitMultiplePhotos;
            var raw = multi ? RenderMultiPhoto(profile.Dpi) : RenderDocument(profile.Dpi, i + 1);
            onPage(new RawScan { Image = raw, Dpi = profile.Dpi, DriverDid = DriverCapabilities.None });
        }
    }

    private Image<Rgba32> RenderDocument(int dpi, int pageNumber)
    {
        // Oversized "glass" with a slightly skewed document near the centre.
        int w = (int)(8.5 * dpi);
        int h = (int)(11.0 * dpi);
        var img = new Image<Rgba32>(w, h, new Rgba32(232, 232, 235)); // light platen

        float skew = (float)((_rng.NextDouble() * 8) - 4); // -4..+4 degrees
        int docW = (int)(w * 0.62);
        int docH = (int)(h * 0.7);
        int x = (w - docW) / 2 + _rng.Next(-40, 40);
        int y = (h - docH) / 2 + _rng.Next(-40, 40);

        using var doc = new Image<Rgba32>(docW, docH, Color.White);
        doc.Mutate(c =>
        {
            // Fake text lines.
            for (int ly = 40; ly < docH - 40; ly += 46)
            {
                float lineW = docW * (0.5f + (float)_rng.NextDouble() * 0.35f);
                c.Fill(new Rgba32(40, 40, 40), new RectangularPolygon(40, ly, lineW - 80, 14));
            }
        });
        using var skewed = doc.Clone(c => c.Rotate(skew).BackgroundColor(Color.White));

        img.Mutate(c => c.DrawImage(skewed, new Point(x, y), 1f));
        return img;
    }

    private Image<Rgba32> RenderMultiPhoto(int dpi)
    {
        int w = (int)(8.5 * dpi);
        int h = (int)(11.0 * dpi);
        var img = new Image<Rgba32>(w, h, new Rgba32(228, 228, 232));

        var palette = new[]
        {
            new Rgba32(180, 90, 70), new Rgba32(70, 120, 170),
            new Rgba32(90, 150, 100), new Rgba32(170, 150, 80)
        };

        // Two photos placed apart so the splitter finds two items.
        var rects = new[]
        {
            new Rectangle((int)(w * 0.08), (int)(h * 0.08), (int)(w * 0.38), (int)(h * 0.34)),
            new Rectangle((int)(w * 0.52), (int)(h * 0.55), (int)(w * 0.40), (int)(h * 0.36))
        };

        img.Mutate(c =>
        {
            for (int i = 0; i < rects.Length; i++)
            {
                c.Fill(palette[i % palette.Length],
                    new RectangularPolygon(rects[i].X, rects[i].Y, rects[i].Width, rects[i].Height));
            }
        });
        return img;
    }

    public void Dispose()
    {
    }
}
