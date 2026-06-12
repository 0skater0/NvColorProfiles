namespace nv_color_profiles.core.rules;

/// <summary>How a rule matches the current foreground window.</summary>
public enum match_type
{
    /// <summary>Executable name of the foreground window's process (with or without ".exe").</summary>
    process,

    /// <summary>Regular expression (case-insensitive) against the foreground window title.</summary>
    window_title,
}

/// <summary>
/// One auto-switch rule: when the foreground window matches, the named <see cref="profile"/> is
/// applied. Rules are evaluated by ascending <see cref="priority"/> (lower number wins first).
/// </summary>
public sealed record rule
{
    public int priority { get; init; }
    public match_type type { get; init; }
    public string value { get; init; } = "";
    public string profile { get; init; } = "";
}
