using ScanApp.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace ScanApp.Core.Export;

/// <summary>
/// Combines a batch of scanned pages into a single multi-page PDF. Each page is JPEG-encoded and
/// embedded; the PDF page is sized to the physical dimensions of the scan (pixels / dpi) so printed
/// output is true-to-size.
/// </summary>
public static class PdfExporter
{
    /// <summary>Writes all <paramref name="pages"/> to a multi-page PDF at <paramref name="path"/>.</summary>
    public static void Save(IReadOnlyList<ScannedPage> pages, string path, int jpegQuality = 90)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
        {
            throw new ArgumentException("No pages to export.", nameof(pages));
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var document = new PdfDocument();
        // PdfSharp embeds image data lazily at Save(); keep encoded streams alive until then.
        var streams = new List<MemoryStream>();
        var images = new List<XImage>();
        try
        {
            foreach (var page in pages)
            {
                var ms = new MemoryStream();
                page.Image.SaveAsJpeg(ms, new JpegEncoder { Quality = Math.Clamp(jpegQuality, 1, 100) });
                ms.Position = 0;
                streams.Add(ms);

                var xImage = XImage.FromStream(ms);
                images.Add(xImage);

                int dpi = page.Dpi > 0 ? page.Dpi : 300;
                double widthPt = page.Width / (double)dpi * 72.0;
                double heightPt = page.Height / (double)dpi * 72.0;

                var pdfPage = document.AddPage();
                pdfPage.Width = XUnit.FromPoint(widthPt);
                pdfPage.Height = XUnit.FromPoint(heightPt);

                using var gfx = XGraphics.FromPdfPage(pdfPage);
                gfx.DrawImage(xImage, 0, 0, widthPt, heightPt);
            }

            document.Save(path);
        }
        finally
        {
            foreach (var img in images)
            {
                img.Dispose();
            }
            foreach (var ms in streams)
            {
                ms.Dispose();
            }
        }
    }
}
