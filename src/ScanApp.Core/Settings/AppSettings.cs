using ScanApp.Core.Models;
using ScanApp.Core.Naming;

namespace ScanApp.Core.Settings;

/// <summary>Application colour theme.</summary>
public enum ThemeMode
{
    Dark,
    Light
}

/// <summary>
/// Persisted user preferences. Serialized to <c>settings.json</c> by <see cref="SettingsStore"/>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Dark or light UI theme.</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    /// <summary>Accent colour as #AARRGGBB (or #RRGGBB) hex; drives buttons/highlights.</summary>
    public string AccentColor { get; set; } = "#FF4F8CFF";


    /// <summary>Last scanner device id used, so the app can reselect it on launch.</summary>
    public string? LastDeviceId { get; set; }

    /// <summary>Destination folder for saved scans. Empty means "ask / use default".</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    public string FileNameTemplate { get; set; } = Naming.FileNameTemplate.Default;

    public OutputFormat ImageFormat { get; set; } = OutputFormat.Jpeg;

    public int JpegQuality { get; set; } = 90;

    public ScanProfile FlatbedProfile { get; set; } = ScanProfile.ForMode(ScanMode.BulkFlatbed);

    public ScanProfile SheetfedProfile { get; set; } = ScanProfile.ForMode(ScanMode.Sheetfed);

    /// <summary>Returns the stored profile for a mode (so per-mode defaults persist independently).</summary>
    public ScanProfile ProfileFor(ScanMode mode) =>
        mode == ScanMode.Sheetfed ? SheetfedProfile : FlatbedProfile;
}
