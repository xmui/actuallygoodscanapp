using ScanApp.Core.Export;
using ScanApp.Core.Models;
using ScanApp.Core.Naming;
using ScanApp.Core.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ScanApp.Core.Tests;

public class ExportAndNamingTests
{
    private static ScannedPage MakePage(int w = 400, int h = 300) =>
        new(TestImages.WhiteWith(w, h, new Rectangle(50, 50, 100, 80)), 200);

    [Theory]
    [InlineData(OutputFormat.Jpeg, ".jpg")]
    [InlineData(OutputFormat.Png, ".png")]
    [InlineData(OutputFormat.Tiff, ".tif")]
    [InlineData(OutputFormat.Webp, ".webp")]
    public void ImageExporter_WritesLoadableFile(OutputFormat format, string ext)
    {
        using var page = MakePage();
        string path = Path.Combine(Path.GetTempPath(), $"scan_test_{Guid.NewGuid():N}{ext}");
        try
        {
            ImageExporter.Save(page, path, format);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);

            // Re-load to confirm the file is a valid image of the right size.
            using var reloaded = Image.Load<Rgba32>(path);
            Assert.Equal(page.Width, reloaded.Width);
            Assert.Equal(page.Height, reloaded.Height);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PdfExporter_WritesMultiPagePdf()
    {
        var pages = new List<ScannedPage> { MakePage(), MakePage(500, 400) };
        string path = Path.Combine(Path.GetTempPath(), $"scan_test_{Guid.NewGuid():N}.pdf");
        try
        {
            PdfExporter.Save(pages, path);
            Assert.True(File.Exists(path));

            // Valid PDFs start with the %PDF- header.
            using var fs = File.OpenRead(path);
            var header = new byte[5];
            fs.ReadExactly(header);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(header));
        }
        finally
        {
            foreach (var p in pages) p.Dispose();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FileNameTemplate_ExpandsTokens()
    {
        var now = new DateTime(2026, 6, 30, 14, 5, 9);
        string result = FileNameTemplate.Expand("Scan_{date}_{counter:000}", 7, now);
        Assert.Equal("Scan_2026-06-30_007", result);
    }

    [Fact]
    public void FileNameTemplate_StripsInvalidCharacters()
    {
        string result = FileNameTemplate.Expand("a/b:c*d", 1, DateTime.Now);
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            Assert.DoesNotContain(ch, result);
        }
    }

    [Fact]
    public void ResolveUniquePath_AvoidsCollisions()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("/out", "Scan.jpg"),
            Path.Combine("/out", "Scan (1).jpg")
        };

        string path = FileNameTemplate.ResolveUniquePath("/out", "Scan", ".jpg", p => existing.Contains(p));
        Assert.Equal(Path.Combine("/out", "Scan (2).jpg"), path);
    }

    [Fact]
    public void SettingsStore_RoundTripsAndDetectsPortable()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"scanapp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Mark the directory portable.
            File.WriteAllText(Path.Combine(dir, SettingsStore.PortableMarkerName), "");
            var store = new SettingsStore(dir);
            Assert.True(store.IsPortable);
            Assert.StartsWith(dir, store.DataDirectory);

            var settings = store.Load();
            settings.FileNameTemplate = "MyScan_{counter}";
            settings.ImageFormat = OutputFormat.Png;
            store.Save(settings);

            var reloaded = new SettingsStore(dir).Load();
            Assert.Equal("MyScan_{counter}", reloaded.FileNameTemplate);
            Assert.Equal(OutputFormat.Png, reloaded.ImageFormat);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
