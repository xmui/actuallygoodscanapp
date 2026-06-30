using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanApp.Core.Models;

/// <summary>
/// A single scanned page held in memory as an ImageSharp image plus metadata. UI-agnostic so the
/// same model is used by the core pipeline, the exporters and (after conversion) the WPF preview.
/// </summary>
public sealed class ScannedPage : IDisposable
{
    private Image<Rgba32> _image;

    public ScannedPage(Image<Rgba32> image, int dpi = 300)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        Dpi = dpi <= 0 ? 300 : dpi;
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }

    /// <summary>Resolution the page was scanned at, used for fixed-size cropping and PDF sizing.</summary>
    public int Dpi { get; set; }

    public Image<Rgba32> Image => _image;

    public int Width => _image.Width;

    public int Height => _image.Height;

    /// <summary>Replaces the underlying image, disposing the previous one.</summary>
    public void ReplaceImage(Image<Rgba32> newImage)
    {
        ArgumentNullException.ThrowIfNull(newImage);
        if (ReferenceEquals(newImage, _image))
        {
            return;
        }

        var old = _image;
        _image = newImage;
        old.Dispose();
    }

    public void Dispose() => _image.Dispose();
}
