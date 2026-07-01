using System.Text.Json;
using System.Text.Json.Serialization;
using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanApp.Core.Projects;

/// <summary>A page to persist: its pristine original image + dpi + edit state.</summary>
public sealed record ProjectPageInput(Image<Rgba32> Original, int Dpi, PageEdits Edits);

/// <summary>A page loaded from a project: the original image + dpi + edit state to re-apply.</summary>
public sealed record ProjectPageData(Image<Rgba32> Image, int Dpi, PageEdits Edits);

/// <summary>
/// Saves/loads a project as a folder: each page's pristine original stored as **lossless WebP**
/// (small, but fully re-editable) plus a <c>project.json</c> manifest of per-page edits and order.
/// Because originals are kept, pages remain fully editable even after exporting.
/// </summary>
public static class ProjectStore
{
    public const string ManifestName = "project.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class Manifest
    {
        public int Version { get; set; } = 1;
        public List<Entry> Pages { get; set; } = new();
    }

    private sealed class Entry
    {
        public string File { get; set; } = "";
        public int Dpi { get; set; } = 300;
        public PageEdits Edits { get; set; } = PageEdits.Defaults;
    }

    public static void Save(string projectDir, IReadOnlyList<ProjectPageInput> pages)
    {
        ArgumentNullException.ThrowIfNull(projectDir);
        ArgumentNullException.ThrowIfNull(pages);
        Directory.CreateDirectory(projectDir);

        var encoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossless };
        var manifest = new Manifest();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < pages.Count; i++)
        {
            string file = $"page-{i + 1:000}.webp";
            pages[i].Original.SaveAsWebp(Path.Combine(projectDir, file), encoder);
            written.Add(file);
            manifest.Pages.Add(new Entry { File = file, Dpi = pages[i].Dpi, Edits = pages[i].Edits });
        }

        File.WriteAllText(Path.Combine(projectDir, ManifestName), JsonSerializer.Serialize(manifest, Json));

        // Remove stale page images from a previous, larger save.
        foreach (var old in Directory.EnumerateFiles(projectDir, "page-*.webp"))
        {
            if (!written.Contains(Path.GetFileName(old)))
            {
                try { File.Delete(old); } catch { /* best effort */ }
            }
        }
    }

    public static List<ProjectPageData> Load(string projectDir)
    {
        ArgumentNullException.ThrowIfNull(projectDir);
        var json = File.ReadAllText(Path.Combine(projectDir, ManifestName));
        var manifest = JsonSerializer.Deserialize<Manifest>(json, Json) ?? new Manifest();

        var result = new List<ProjectPageData>();
        foreach (var entry in manifest.Pages)
        {
            var path = Path.Combine(projectDir, entry.File);
            if (!File.Exists(path))
            {
                continue;
            }
            var image = Image.Load<Rgba32>(path);
            result.Add(new ProjectPageData(image, entry.Dpi, entry.Edits ?? PageEdits.Defaults));
        }
        return result;
    }

    /// <summary>True if the folder looks like a project (has a manifest).</summary>
    public static bool IsProject(string projectDir) =>
        File.Exists(Path.Combine(projectDir, ManifestName));
}
