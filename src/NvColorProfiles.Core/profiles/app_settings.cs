namespace nv_color_profiles.core.profiles;

/// <summary>Persisted application settings (the "settings" object of the config).</summary>
public sealed record app_settings
{
    /// <summary>"manual" = fixed active profile; "auto" = rule-driven switching.</summary>
    public string mode { get; init; } = "manual";

    /// <summary>UI language: "auto" (detect from the OS), "de" or "en".</summary>
    public string language { get; init; } = "auto";

    public bool autostart { get; init; }
    public bool start_minimized { get; init; } = true;

    /// <summary>Restore displays to their captured baseline when the app exits (default on).</summary>
    public bool restore_on_exit { get; init; } = true;

    /// <summary>Verbose diagnostic logging (rule evaluation / hardware apply).</summary>
    public bool diagnostic_logging { get; init; }

    /// <summary>Active profile in manual mode.</summary>
    public string active_profile { get; init; } = "Default";

    /// <summary>Profile used in auto mode when no rule matches.</summary>
    public string fallback_profile { get; init; } = "Default";

    /// <summary>
    /// Delay (ms) the foreground window must stay focused before an auto-switch applies. Higher =
    /// brief alt-tabs don't trigger a switch. Default 150 (just a flicker guard).
    /// </summary>
    public int switch_delay_ms { get; init; } = 150;

    /// <summary>Register the global hotkeys (cycle profile, toggle auto). Default on.</summary>
    public bool hotkeys_enabled { get; init; } = true;

    // global hotkey bindings — defaults match the original fixed combos (Ctrl+Alt+...).
    // mods 0x0003 = MOD_CONTROL | MOD_ALT; keys: 0x22 PageDown, 0x21 PageUp, 0x41 'A'.
    public hotkey_binding hotkey_next { get; init; } = new() { mods = 0x0003, key = 0x22 };
    public hotkey_binding hotkey_prev { get; init; } = new() { mods = 0x0003, key = 0x21 };
    public hotkey_binding hotkey_toggle { get; init; } = new() { mods = 0x0003, key = 0x41 };

    /// <summary>User opted into the daily GitHub release check. Only ever consulted after
    /// <see cref="update_check_prompted"/> is true, so the first-run modal always decides.</summary>
    public bool update_check_enabled { get; init; } = true;

    /// <summary>Set once the user has answered the first-run "check for updates" dialog. Until then
    /// the app must not make any network requests, regardless of <see cref="update_check_enabled"/>.</summary>
    public bool update_check_prompted { get; init; }

    /// <summary>Tag returned by the last successful GitHub check (without a leading "v"). Kept so we
    /// can show the badge across restarts and skip the toast when the user has already seen it.</summary>
    public string? latest_seen_version { get; init; }

    /// <summary>UTC timestamp of the last check attempt (success or error). Drives the 24h cadence.</summary>
    public DateTime? last_update_check_at { get; init; }
}
