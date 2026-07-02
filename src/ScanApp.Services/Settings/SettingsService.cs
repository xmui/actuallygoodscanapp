using Microsoft.Extensions.Logging;
using ScanApp.Core.Settings;

namespace ScanApp.Services.Settings;

/// <summary>
/// Owns the loaded <see cref="AppSettings"/> instance and its persistence, so view-models share one
/// settings object and never touch the filesystem directly.
/// </summary>
public sealed class SettingsService
{
    private readonly SettingsStore _store;
    private readonly ILogger<SettingsService> _log;

    public SettingsService(SettingsStore store, ILogger<SettingsService> log)
    {
        _store = store;
        _log = log;
        Current = _store.Load();
        if (string.IsNullOrWhiteSpace(Current.OutputDirectory))
        {
            Current.OutputDirectory = _store.DefaultOutputDirectory;
        }
    }

    public AppSettings Current { get; }

    public string DataDirectory => _store.DataDirectory;
    public string DefaultOutputDirectory => _store.DefaultOutputDirectory;
    public bool IsPortable => _store.IsPortable;

    /// <summary>Persists the current settings (best-effort; never throws into the UI).</summary>
    public void Save()
    {
        try
        {
            _store.Save(Current);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Settings save failed");
        }
    }
}
