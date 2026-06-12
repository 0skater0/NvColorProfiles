using nv_color_profiles.core.display;
using nv_color_profiles.core.rules;

namespace nv_color_profiles.core.profiles;

/// <summary>Root of the persisted configuration. <see cref="schema_version"/> guards future migrations.</summary>
public sealed record app_config
{
    public const string DEFAULT_PROFILE_NAME = "Default";

    public int schema_version { get; init; } = 1;
    public app_settings settings { get; init; } = new();
    public List<profile> profiles { get; init; } = new();
    public List<rule> rules { get; init; } = new();
    public List<schedule_entry> schedules { get; init; } = new();

    /// <summary>A fresh config containing only the read-only neutral Default profile.</summary>
    public static app_config create_default() => new()
    {
        profiles = { profile.uniform(DEFAULT_PROFILE_NAME, color_settings.neutral, builtin: true) },
    };

    /// <summary>Looks up a profile by name (case-insensitive), or null.</summary>
    public profile? find_profile(string name)
        => profiles.FirstOrDefault(p => string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Replaces null collections with empty ones. System.Text.Json overrides the `= new()` member
    /// initializers when the JSON contains an explicit null (e.g. a hand-edited or truncated import
    /// file with "displays": null), which would otherwise NRE downstream. Call before
    /// <see cref="with_default_ensured"/>, which itself dereferences <see cref="profiles"/>.
    /// </summary>
    public app_config sanitized()
    {
        var safe_settings = settings ?? new();
        var defaults = new app_settings(); // a null binding means "use the default combo", not "unset"
        safe_settings = safe_settings with
        {
            language = safe_settings.language ?? "auto",
            hotkey_next = safe_settings.hotkey_next ?? defaults.hotkey_next,
            hotkey_prev = safe_settings.hotkey_prev ?? defaults.hotkey_prev,
            hotkey_toggle = safe_settings.hotkey_toggle ?? defaults.hotkey_toggle,
        };
        return this with
        {
            settings = safe_settings,
            profiles = (profiles ?? new())
                .Select(p => p.displays is null ? p with { displays = new() } : p)
                .ToList(),
            rules = rules ?? new(),
            schedules = schedules ?? new(),
        };
    }

    /// <summary>Ensures a read-only Default profile exists, prepending one if missing.</summary>
    public app_config with_default_ensured()
    {
        if (find_profile(DEFAULT_PROFILE_NAME) is not null)
        {
            return this;
        }

        var ensured = new List<profile> { profile.uniform(DEFAULT_PROFILE_NAME, color_settings.neutral, builtin: true) };
        ensured.AddRange(profiles);
        return this with { profiles = ensured };
    }
}
