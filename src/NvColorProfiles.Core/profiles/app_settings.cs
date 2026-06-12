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
}
