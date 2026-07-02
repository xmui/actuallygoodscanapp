using Microsoft.Extensions.Logging;
using ScanApp.Core.Projects;

namespace ScanApp.Services.Projects;

/// <summary>
/// Project + session persistence with recents tracking, on top of <see cref="ProjectStore"/>.
/// The "session" is a hidden project under the data dir that auto-restores on launch.
/// </summary>
public sealed class ProjectService
{
    private const int MaxRecents = 8;
    private readonly ILogger<ProjectService> _log;

    public ProjectService(string dataDirectory, ILogger<ProjectService> log)
    {
        _log = log;
        SessionDirectory = Path.Combine(dataDirectory, "Session");
    }

    public string SessionDirectory { get; }

    public bool IsProject(string dir) => ProjectStore.IsProject(dir);

    public void Save(string dir, IReadOnlyList<ProjectPageInput> pages)
    {
        ProjectStore.Save(dir, pages);
        _log.LogInformation("Saved project ({Count} pages) to {Dir}", pages.Count, dir);
    }

    public List<ProjectPageData> Load(string dir)
    {
        var pages = ProjectStore.Load(dir);
        _log.LogInformation("Loaded project ({Count} pages) from {Dir}", pages.Count, dir);
        return pages;
    }

    public void SaveSession(IReadOnlyList<ProjectPageInput> pages)
    {
        try
        {
            if (pages.Count == 0)
            {
                if (Directory.Exists(SessionDirectory))
                {
                    Directory.Delete(SessionDirectory, recursive: true);
                }
                return;
            }
            ProjectStore.Save(SessionDirectory, pages);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Session auto-save failed"); // best-effort by design
        }
    }

    public List<ProjectPageData>? TryLoadSession()
    {
        try
        {
            return ProjectStore.IsProject(SessionDirectory) ? ProjectStore.Load(SessionDirectory) : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Session restore failed; starting empty");
            return null;
        }
    }

    /// <summary>Returns the recents list with <paramref name="dir"/> promoted to the front.</summary>
    public static List<string> Promote(IEnumerable<string> recents, string dir)
    {
        var list = recents.Where(r => !string.Equals(r, dir, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(0, dir);
        if (list.Count > MaxRecents)
        {
            list.RemoveRange(MaxRecents, list.Count - MaxRecents);
        }
        return list;
    }
}
