using ScanApp.Core.Models;
using ScanApp.Core.Projects;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ScanApp.Core.Tests;

public class ProjectStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsImagesAndEdits()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"agsproj_{Guid.NewGuid():N}");
        var inputs = new List<ProjectPageInput>
        {
            new(TestImages.WhiteWith(300, 200, new Rectangle(50, 40, 120, 80)), 300,
                new PageEdits { RotationDegrees = 4.5, Brightness = 10, AutoLevel = true, CropActive = true, Crop = new NormalizedRect(0.1, 0.1, 0.8, 0.8) }),
            new(TestImages.WhiteWith(400, 260, new Rectangle(60, 60, 160, 100)), 200,
                new PageEdits { Contrast = -15, RemoveDust = true })
        };

        try
        {
            ProjectStore.Save(dir, inputs);
            Assert.True(ProjectStore.IsProject(dir));

            var loaded = ProjectStore.Load(dir);
            Assert.Equal(2, loaded.Count);

            Assert.Equal(300, loaded[0].Image.Width);
            Assert.Equal(200, loaded[0].Image.Height);
            Assert.Equal(300, loaded[0].Dpi);
            Assert.Equal(4.5, loaded[0].Edits.RotationDegrees);
            Assert.Equal(10, loaded[0].Edits.Brightness);
            Assert.True(loaded[0].Edits.CropActive);
            Assert.Equal(0.8, loaded[0].Edits.Crop.Width, 3);

            Assert.Equal(-15, loaded[1].Edits.Contrast);
            Assert.True(loaded[1].Edits.RemoveDust);

            foreach (var p in loaded) p.Image.Dispose();
        }
        finally
        {
            foreach (var i in inputs) i.Original.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
