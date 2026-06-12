using System.Text.RegularExpressions;

namespace nv_color_profiles.core.rules;

/// <summary>
/// Resolves which profile a foreground window should activate: the first rule (by ascending
/// priority) whose match succeeds wins; if none match, the caller falls back to its fallback profile.
/// </summary>
public static class rule_engine
{
    private static readonly TimeSpan regex_timeout = TimeSpan.FromMilliseconds(50);

    /// <summary>Returns the target profile name of the first matching rule, or null if none match.</summary>
    public static string? evaluate(IEnumerable<rule> rules, string process_name, string window_title)
    {
        foreach (var r in rules.OrderBy(r => r.priority))
        {
            if (matches(r, process_name, window_title))
            {
                return r.profile;
            }
        }
        return null;
    }

    private static bool matches(rule r, string process_name, string window_title) => r.type switch
    {
        match_type.process => process_matches(r.value, process_name),
        match_type.window_title => title_matches(r.value, window_title),
        _ => false,
    };

    private static bool process_matches(string pattern, string process_name)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(process_name))
        {
            return false;
        }
        return string.Equals(strip_exe(pattern), strip_exe(process_name), StringComparison.OrdinalIgnoreCase);
    }

    private static string strip_exe(string name)
        => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    private static bool title_matches(string pattern, string window_title)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrEmpty(window_title))
        {
            return false;
        }
        try
        {
            return Regex.IsMatch(window_title, pattern, RegexOptions.IgnoreCase, regex_timeout);
        }
        catch
        {
            // invalid pattern or catastrophic backtracking timeout — treat as no match
            return false;
        }
    }
}
