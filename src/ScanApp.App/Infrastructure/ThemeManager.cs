using System.Windows;
using System.Windows.Media;
using ScanApp.Core.Settings;

namespace ScanApp.App.Infrastructure;

/// <summary>An accent colour choice shown as a clickable swatch.</summary>
public sealed record AccentSwatch(string Name, string Hex, Brush Brush);

/// <summary>
/// Applies the colour theme at runtime by mutating the shared brush resources defined in App.xaml.
/// Because those brushes are not frozen, changing <see cref="SolidColorBrush.Color"/> updates every
/// control that references them (Static or Dynamic) live — so theme/accent changes apply instantly.
/// </summary>
public static class ThemeManager
{
    /// <summary>Built-in accent choices shown as swatches in the Theme panel.</summary>
    public static readonly (string Name, string Hex)[] AccentPresets =
    {
        ("Blue", "#FF4F8CFF"),
        ("Violet", "#FF8B7CFF"),
        ("Green", "#FF36B37E"),
        ("Amber", "#FFE0A33C"),
        ("Rose", "#FFE5557A"),
        ("Teal", "#FF2BB6C4")
    };

    /// <summary>Accent presets as bindable swatches (name, hex, preview brush).</summary>
    public static IReadOnlyList<AccentSwatch> Swatches { get; } = AccentPresets
        .Select(p => new AccentSwatch(p.Name, p.Hex, new SolidColorBrush(Parse(p.Hex, Colors.Gray))))
        .ToList();

    public static void Apply(AppSettings settings)
    {
        var res = Application.Current?.Resources;
        if (res is null)
        {
            return;
        }

        bool light = settings.Theme == ThemeMode.Light;
        if (light)
        {
            Set(res, "Bg", 0xF4, 0xF5, 0xF7);
            Set(res, "Panel", 0xFF, 0xFF, 0xFF);
            Set(res, "PanelAlt", 0xEC, 0xEE, 0xF2);
            Set(res, "Text", 0x1E, 0x20, 0x26);
            Set(res, "TextDim", 0x5C, 0x5F, 0x6A);
            Set(res, "Border", 0xD8, 0xDA, 0xE0);
            Set(res, "InputBg", 0xFF, 0xFF, 0xFF);
            Set(res, "InputBorder", 0xC7, 0xCA, 0xD2);
            Set(res, "Hover", 0xE3, 0xE5, 0xEA);
        }
        else
        {
            Set(res, "Bg", 0x1E, 0x1F, 0x26);
            Set(res, "Panel", 0x27, 0x28, 0x32);
            Set(res, "PanelAlt", 0x32, 0x33, 0x3F);
            Set(res, "Text", 0xED, 0xED, 0xF2);
            Set(res, "TextDim", 0xA4, 0xA6, 0xB3);
            Set(res, "Border", 0x3A, 0x3B, 0x47);
            Set(res, "InputBg", 0x2C, 0x2D, 0x38);
            Set(res, "InputBorder", 0x3E, 0x40, 0x50);
            Set(res, "Hover", 0x3E, 0x40, 0x50);
        }

        var accent = Parse(settings.AccentColor, Color.FromArgb(0xFF, 0x4F, 0x8C, 0xFF));
        SetColor(res, "Accent", accent);
        SetColor(res, "AccentHover", Lighten(accent, 0.14));
    }

    private static void Set(ResourceDictionary res, string key, byte r, byte g, byte b) =>
        SetColor(res, key, Color.FromArgb(0xFF, r, g, b));

    private static void SetColor(ResourceDictionary res, string key, Color c)
    {
        if (res[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = c;
        }
        else
        {
            res[key] = new SolidColorBrush(c);
        }
    }

    private static Color Lighten(Color c, double f)
    {
        byte L(byte x) => (byte)Math.Min(255, x + ((255 - x) * f));
        return Color.FromArgb(c.A, L(c.R), L(c.G), L(c.B));
    }

    public static Color Parse(string hex, Color fallback)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c)
            {
                return c;
            }
        }
        catch
        {
            // fall through
        }
        return fallback;
    }
}
