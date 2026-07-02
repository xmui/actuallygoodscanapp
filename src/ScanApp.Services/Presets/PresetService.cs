using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ScanApp.Services.Presets;

/// <summary>Loads/saves the user's named scan presets as <c>presets.json</c> in the app data dir.</summary>
public sealed class PresetService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly ILogger<PresetService> _log;
    private List<ScanPreset> _presets;

    public PresetService(string dataDirectory, ILogger<PresetService> log)
    {
        _path = Path.Combine(dataDirectory, "presets.json");
        _log = log;
        _presets = LoadFromDisk();
    }

    public IReadOnlyList<ScanPreset> Presets => _presets;

    public event EventHandler? PresetsChanged;

    /// <summary>Adds or replaces (by name, case-insensitive) and persists.</summary>
    public void SavePreset(ScanPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _presets.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        _presets.Add(preset);
        _presets = _presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Persist();
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeletePreset(string name)
    {
        if (_presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Persist();
            PresetsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private List<ScanPreset> LoadFromDisk()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<List<ScanPreset>>(File.ReadAllText(_path), Json);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not load presets from {Path}; starting empty", _path);
        }
        return new List<ScanPreset>();
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_presets, Json));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not save presets to {Path}", _path);
        }
    }
}
