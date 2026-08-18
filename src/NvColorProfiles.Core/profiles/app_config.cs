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

    /// <summary>
    /// Returns a new config where the profile called <paramref name="old_name"/> is renamed to
    /// <paramref name="new_name"/>, and every reference that pointed at the old name is rewritten
    /// to the new one: the profile itself, <see cref="app_settings.active_profile"/>,
    /// <see cref="app_settings.fallback_profile"/>, and every <c>profile</c> field in
    /// <see cref="rules"/> and <see cref="schedules"/>. Matches are case-insensitive so a rule
    /// stored with a different casing still gets rewired. No-op when the two names are equal.
    /// </summary>
    public app_config with_profile_renamed(string old_name, string new_name)
    {
        if (string.Equals(old_name, new_name, StringComparison.Ordinal))
        {
            return this;
        }

        static bool eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        var new_settings = settings;
        if (eq(new_settings.active_profile, old_name))
        {
            new_settings = new_settings with { active_profile = new_name };
        }
        if (eq(new_settings.fallback_profile, old_name))
        {
            new_settings = new_settings with { fallback_profile = new_name };
        }

        var new_profiles = profiles
            .Select(p => eq(p.name, old_name) ? p with { name = new_name } : p)
            .ToList();
        var new_rules = rules
            .Select(r => eq(r.profile, old_name) ? r with { profile = new_name } : r)
            .ToList();
        var new_schedules = schedules
            .Select(s => eq(s.profile, old_name) ? s with { profile = new_name } : s)
            .ToList();

        return this with
        {
            settings = new_settings,
            profiles = new_profiles,
            rules = new_rules,
            schedules = new_schedules,
        };
    }
}
