using Microsoft.Extensions.Logging;
using ScanApp.Core.Export;
using ScanApp.Core.Models;
using ScanApp.Core.Naming;
using ScanApp.Services.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanApp.Services.Export;

/// <summary>One page ready for export: pristine original + the edits to bake in.</summary>
public sealed record ExportPage(Image<Rgba32> Original, PageEdits Edits, int Dpi);

/// <summary>
/// Batch export: renders each page through the canonical <see cref="EditRenderer"/> (crop applied)
/// and writes images or a multi-page PDF with collision-safe names.
/// </summary>
public sealed class ExportService
{
    private readonly ILogger<ExportService> _log;

    public ExportService(ILogger<ExportService> log) => _log = log;

    /// <summary>Saves each page as an image file. Returns the paths written.</summary>
    public List<string> SaveImages(
        IReadOnlyList<ExportPage> pages,
        string directory,
        string fileNameTemplate,
        OutputFormat format,
        int jpegQuality)
    {
        Directory.CreateDirectory(directory);
        var now = DateTime.Now;
        var written = new List<string>();

        for (int i = 0; i < pages.Count; i++)
        {
            string baseName = FileNameTemplate.Expand(fileNameTemplate, i + 1, now);
            string path = FileNameTemplate.ResolveUniquePath(directory, baseName, format.Extension(), File.Exists);

            using var rendered = EditRenderer.RenderForExport(pages[i].Original, pages[i].Edits);
            using var page = new ScannedPage(rendered.Clone(), pages[i].Dpi);
            ImageExporter.Save(page, path, format, jpegQuality);
            written.Add(path);
        }

        _log.LogInformation("Exported {Count} images to {Dir}", written.Count, directory);
        return written;
    }

    /// <summary>Saves all pages as one multi-page PDF. Returns the path written.</summary>
    public string SavePdf(IReadOnlyList<ExportPage> pages, string directory, string fileNameTemplate, int jpegQuality)
    {
        Directory.CreateDirectory(directory);
        string baseName = FileNameTemplate.Expand(fileNameTemplate, 1, DateTime.Now);
        string path = FileNameTemplate.ResolveUniquePath(directory, baseName, ".pdf", File.Exists);

        var rendered = new List<ScannedPage>();
        try
        {
            foreach (var p in pages)
            {
                rendered.Add(new ScannedPage(EditRenderer.RenderForExport(p.Original, p.Edits), p.Dpi));
            }
            PdfExporter.Save(rendered, path, jpegQuality);
        }
        finally
        {
            foreach (var r in rendered)
            {
                r.Dispose();
            }
        }

        _log.LogInformation("Exported {Count}-page PDF to {Path}", pages.Count, path);
        return path;
    }

    /// <summary>Composes a filename template from friendly builder parts (prefix/date/counter).</summary>
    public static string ComposeTemplate(string prefix, bool includeDate, bool includeCounter, int counterDigits)
    {
        var parts = new List<string> { string.IsNullOrWhiteSpace(prefix) ? "Scan" : prefix.Trim() };
        if (includeDate)
        {
            parts.Add("{date}");
        }
        if (includeCounter)
        {
            parts.Add("{counter:" + new string('0', Math.Clamp(counterDigits, 1, 6)) + "}");
        }
        else if (!includeDate)
        {
            parts.Add("{counter}"); // keep names unique no matter what
        }
        return string.Join("_", parts);
    }
}
