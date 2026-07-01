namespace ScanApp.Core.Models;

/// <summary>Per-image output formats. PDF is handled separately as a multi-page batch export.</summary>
public enum OutputFormat
{
    Jpeg,
    Png,
    Tiff,
    Webp
}

public static class OutputFormatExtensions
{
    public static string Extension(this OutputFormat format) => format switch
    {
        OutputFormat.Jpeg => ".jpg",
        OutputFormat.Png => ".png",
        OutputFormat.Tiff => ".tif",
        OutputFormat.Webp => ".webp",
        _ => ".jpg"
    };
}
