using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScanApp.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> with portable-aware path resolution.
///
/// Portable mode: if a file named <c>portable.marker</c> (or a writable <c>Data</c> folder) exists
/// next to the application executable, settings and the default output folder live under the app
/// directory, so the whole app travels on a USB stick. Otherwise settings live in
/// <c>%LOCALAPPDATA%/ActuallyGoodScanApp</c>.
/// </summary>
public sealed class SettingsStore
{
    public const string PortableMarkerName = "portable.marker";
    public const string PortableDataFolder = "Data";
    private const string AppFolderName = "ActuallyGoodScanApp";
    private const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _settingsPath;

    public SettingsStore(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
        IsPortable = DetectPortable(baseDirectory);
        DataDirectory = IsPortable
            ? Path.Combine(baseDirectory, PortableDataFolder)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);
        _settingsPath = Path.Combine(DataDirectory, SettingsFileName);
    }

    public string BaseDirectory { get; }

    /// <summary>True when running as a portable install.</summary>
    public bool IsPortable { get; }

    /// <summary>Directory where settings (and, in portable mode, default output) are stored.</summary>
    public string DataDirectory { get; }

    public string SettingsPath => _settingsPath;

    /// <summary>Default scan output folder: a "Scans" subfolder of the data dir (portable) or Documents.</summary>
    public string DefaultOutputDirectory => IsPortable
        ? Path.Combine(DataDirectory, "Scans")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ActuallyGoodScans");

    public static bool DetectPortable(string baseDirectory)
    {
        if (File.Exists(Path.Combine(baseDirectory, PortableMarkerName)))
        {
            return true;
        }

        string dataDir = Path.Combine(baseDirectory, PortableDataFolder);
        return Directory.Exists(dataDir);
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings should never block startup; fall back to defaults.
        }

        return new AppSettings { OutputDirectory = DefaultOutputDirectory };
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
