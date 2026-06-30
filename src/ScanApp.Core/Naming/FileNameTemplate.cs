using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ScanApp.Core.Naming;

/// <summary>
/// Expands a user file-naming template and resolves collision-free output paths so saving a batch
/// is one click. Supported tokens (case-insensitive):
///   {date}            -> yyyy-MM-dd        {date:format} -> custom DateTime format
///   {time}            -> HHmmss
///   {counter}         -> 1-based index     {counter:000}  -> zero-padded
/// Anything else is treated as literal text. Invalid filename characters are stripped.
/// </summary>
public static partial class FileNameTemplate
{
    public const string Default = "Scan_{date}_{counter:000}";

    [GeneratedRegex(@"\{(?<name>date|time|counter)(?::(?<fmt>[^}]+))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    /// <summary>Expands the template into a base file name (no extension) for the given 1-based counter.</summary>
    public static string Expand(string template, int counter, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            template = Default;
        }

        string result = TokenRegex().Replace(template, match =>
        {
            string name = match.Groups["name"].Value.ToLowerInvariant();
            string fmt = match.Groups["fmt"].Success ? match.Groups["fmt"].Value : string.Empty;
            return name switch
            {
                "date" => now.ToString(string.IsNullOrEmpty(fmt) ? "yyyy-MM-dd" : fmt, CultureInfo.InvariantCulture),
                "time" => now.ToString(string.IsNullOrEmpty(fmt) ? "HHmmss" : fmt, CultureInfo.InvariantCulture),
                "counter" => string.IsNullOrEmpty(fmt)
                    ? counter.ToString(CultureInfo.InvariantCulture)
                    : counter.ToString(fmt, CultureInfo.InvariantCulture),
                _ => match.Value
            };
        });

        return Sanitize(result);
    }

    /// <summary>Removes characters not allowed in file names; collapses to a safe fallback if empty.</summary>
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }
        string cleaned = sb.ToString().Trim();
        return cleaned.Length == 0 ? "Scan" : cleaned;
    }

    /// <summary>
    /// Returns a path inside <paramref name="directory"/> for <paramref name="baseName"/> +
    /// <paramref name="extension"/> that does not collide, appending " (1)", " (2)", ... as needed.
    /// <paramref name="exists"/> lets callers inject the existence check (real FS or test double).
    /// </summary>
    public static string ResolveUniquePath(string directory, string baseName, string extension, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        string candidate = Path.Combine(directory, baseName + extension);
        if (!exists(candidate))
        {
            return candidate;
        }

        for (int i = 1; ; i++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({i}){extension}");
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }
}
