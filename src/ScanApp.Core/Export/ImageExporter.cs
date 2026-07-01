using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;

namespace ScanApp.Core.Export;

/// <summary>Saves an individual <see cref="ScannedPage"/> as JPEG, PNG or TIFF.</summary>
public static class ImageExporter
{
    /// <summary>Writes the page to <paramref name="path"/> in the requested format.</summary>
    public static void Save(ScannedPage page, string path, OutputFormat format, int jpegQuality = 90)
    {
        ArgumentNullException.ThrowIfNull(page);
        EnsureDirectory(path);
        StampDpi(page);

        switch (format)
        {
            case OutputFormat.Png:
                page.Image.SaveAsPng(path, new PngEncoder());
                break;
            case OutputFormat.Tiff:
                page.Image.SaveAsTiff(path, new TiffEncoder());
                break;
            case OutputFormat.Webp:
                page.Image.SaveAsWebp(path, new WebpEncoder { Quality = Math.Clamp(jpegQuality, 1, 100) });
                break;
            default:
                page.Image.SaveAsJpeg(path, new JpegEncoder { Quality = Math.Clamp(jpegQuality, 1, 100) });
                break;
        }
    }

    /// <summary>Records the scan resolution in the image metadata so files report the right DPI.</summary>
    private static void StampDpi(ScannedPage page)
    {
        var meta = page.Image.Metadata;
        meta.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
        meta.HorizontalResolution = page.Dpi;
        meta.VerticalResolution = page.Dpi;
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
