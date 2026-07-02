using Microsoft.Extensions.Logging.Abstractions;
using ScanApp.Core.Models;
using ScanApp.Services.Editing;
using ScanApp.Services.Export;
using ScanApp.Services.Presets;
using ScanApp.Services.Projects;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ScanApp.Services.Tests;

public class EditPipelineTests
{
    private static Image<Rgba32> Original() => new(120, 80, new Rgba32(128, 128, 128, 255));

    [Fact]
    public async Task RapidRequests_Coalesce_DeliveringOnlyTheLatest()
    {
        using var original = Original();
        var delivered = new List<PageEdits>();
        using var pipeline = new EditPipeline(
            original,
            (img, edits) => { lock (delivered) { delivered.Add(edits); } img.Dispose(); },
            debounce: TimeSpan.FromMilliseconds(60));

        // Simulate a slider drag: many rapid states.
        for (int b = 1; b <= 20; b++)
        {
            pipeline.Request(new PageEdits { Brightness = b });
            await Task.Delay(5);
        }

        await Task.Delay(400);
        await pipeline.DrainAsync();

        lock (delivered)
        {
            Assert.True(delivered.Count < 20, $"expected coalescing, got {delivered.Count} renders");
            Assert.Equal(20, delivered[^1].Brightness); // final state always wins
        }
    }

    [Fact]
    public async Task Render_AppliesEditChain()
    {
        using var original = Original();
        Image<Rgba32>? result = null;
        using var pipeline = new EditPipeline(
            original,
            (img, _) => Interlocked.Exchange(ref result, img)?.Dispose(),
            debounce: TimeSpan.FromMilliseconds(1));

        pipeline.Request(new PageEdits { Brightness = 50 });
        await Task.Delay(300);
        await pipeline.DrainAsync();

        Assert.NotNull(result);
        result!.ProcessPixelRows(a => Assert.True(a.GetRowSpan(0)[0].R > 150, "brightness should have applied"));
        result.Dispose();
    }
}

public class UndoServiceTests
{
    [Fact]
    public void UndoRedo_RoundTrips()
    {
        int value = 0;
        var undo = new UndoService();
        undo.Push(new UndoableAction("set 1", () => value = 0, () => value = 1));
        value = 1;

        undo.Undo();
        Assert.Equal(0, value);
        Assert.True(undo.CanRedo);

        undo.Redo();
        Assert.Equal(1, value);
    }

    [Fact]
    public void SameKeyWithinWindow_CoalescesToOneStep()
    {
        int value = 0;
        var undo = new UndoService();
        for (int i = 1; i <= 5; i++)
        {
            int captured = i;
            undo.Push(new UndoableAction($"set {captured}", () => value = 0, () => value = captured, CoalesceKey: "slider"));
        }
        value = 5;

        undo.Undo(); // one undo should revert the whole coalesced drag
        Assert.Equal(0, value);
        Assert.False(undo.CanUndo);
    }
}

public class ExportServiceTests
{
    [Theory]
    [InlineData("Scan", true, true, 3, "Scan_{date}_{counter:000}")]
    [InlineData("Trip", false, true, 2, "Trip_{counter:00}")]
    [InlineData("X", true, false, 3, "X_{date}")]
    [InlineData("X", false, false, 3, "X_{counter}")] // uniqueness is always kept
    public void ComposeTemplate_BuildsExpectedTemplates(string prefix, bool date, bool counter, int digits, string expected)
    {
        Assert.Equal(expected, ExportService.ComposeTemplate(prefix, date, counter, digits));
    }

    [Fact]
    public void SaveImages_AppliesEditsAndCrop()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ags_export_{Guid.NewGuid():N}");
        using var original = new Image<Rgba32>(200, 100, new Rgba32(200, 200, 200, 255));
        var edits = new PageEdits { CropActive = true, Crop = new NormalizedRect(0, 0, 0.5, 1.0) };
        var svc = new ExportService(NullLogger<ExportService>.Instance);

        try
        {
            var written = svc.SaveImages(
                new[] { new ExportPage(original, edits, 300) },
                dir, "T_{counter}", OutputFormat.Png, 90);

            Assert.Single(written);
            using var reloaded = Image.Load<Rgba32>(written[0]);
            Assert.Equal(100, reloaded.Width); // cropped to half width
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

public class PresetServiceTests
{
    [Fact]
    public void SaveAndReload_RoundTripsPresets()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ags_presets_{Guid.NewGuid():N}");
        try
        {
            var svc = new PresetService(dir, NullLogger<PresetService>.Instance);
            svc.SavePreset(ScanPreset.From("Photos 600dpi", new ScanProfile { Dpi = 600, Mode = ScanMode.BulkFlatbed }, OutputFormat.Jpeg));
            svc.SavePreset(ScanPreset.From("Docs PDF", new ScanProfile { Dpi = 300, Mode = ScanMode.Sheetfed }, OutputFormat.Png));

            var reloaded = new PresetService(dir, NullLogger<PresetService>.Instance);
            Assert.Equal(2, reloaded.Presets.Count);
            Assert.Equal(600, reloaded.Presets.Single(p => p.Name.StartsWith("Photos")).Dpi);

            reloaded.DeletePreset("Docs PDF");
            Assert.Single(new PresetService(dir, NullLogger<PresetService>.Instance).Presets);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

public class ProjectServiceTests
{
    [Fact]
    public void PromoteRecents_MovesToFrontAndCaps()
    {
        var recents = new List<string> { "a", "b", "c" };
        var result = ProjectService.Promote(recents, "b");
        Assert.Equal(new[] { "b", "a", "c" }, result);

        for (int i = 0; i < 12; i++)
        {
            result = ProjectService.Promote(result, $"p{i}");
        }
        Assert.Equal(8, result.Count);
        Assert.Equal("p11", result[0]);
    }
}
