using nv_color_profiles.core.display;

namespace nv_color_profiles.core.profiles;

/// <summary>
/// A named set of color settings. <see cref="displays"/> maps a display key to its settings;
/// the key is either a stable display id (decimal string) or <see cref="ALL_DISPLAYS"/> ("*")
/// to apply one setting to every display. <see cref="builtin"/> marks the read-only Default profile.
/// </summary>
public sealed record profile
{
    public const string ALL_DISPLAYS = "*";

    public string name { get; init; } = "";
    public bool builtin { get; init; }
    public Dictionary<string, color_settings> displays { get; init; } = new();

    /// <summary>Resolves the settings for a display id: exact key, then wildcard, then neutral.</summary>
    public color_settings settings_for(uint display_id) => settings_for(display_id.ToString());

    /// <summary>Resolves the settings for a display key (an id string or <see cref="ALL_DISPLAYS"/>).</summary>
    public color_settings settings_for(string key)
    {
        if (displays.TryGetValue(key, out var exact))
        {
            return exact;
        }
        if (displays.TryGetValue(ALL_DISPLAYS, out var all))
        {
            return all;
        }
        return color_settings.neutral;
    }

    /// <summary>Builds a profile that applies the same settings to every display.</summary>
    public static profile uniform(string name, color_settings settings, bool builtin = false) => new()
    {
        name = name,
        builtin = builtin,
        displays = new Dictionary<string, color_settings> { [ALL_DISPLAYS] = settings },
    };
}
