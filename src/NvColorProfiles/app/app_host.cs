using Microsoft.Extensions.Logging;
using nv_color_profiles.core;
using nv_color_profiles.core.display;
using nv_color_profiles.core.profiles;
using nv_color_profiles.core.rules;

namespace nv_color_profiles.app;

/// <summary>
/// Owns and wires the headless services (NvAPI session, display catalog/controls/controller,
/// config store, profile service) and the startup color baseline. UI code talks only to this.
/// </summary>
internal sealed class app_host : IDisposable
{
    private readonly ILogger<app_host> log;
    private readonly nv_session session;
    private readonly nv_display_catalog catalog;
    private readonly vibrance_control vibrance;
    private readonly hue_control hue;
    private readonly nv_display_controller controller;
    private readonly profile_store store;
    private readonly profile_service service;
    private readonly color_baseline baseline;

    public app_host(ILoggerFactory loggers)
    {
        log = loggers.CreateLogger<app_host>();

        session = new nv_session(loggers.CreateLogger<nv_session>());
        catalog = new nv_display_catalog(session, loggers.CreateLogger<nv_display_catalog>());
        vibrance = new vibrance_control(session, loggers.CreateLogger<vibrance_control>());
        hue = new hue_control(session, loggers.CreateLogger<hue_control>());
        controller = new nv_display_controller(vibrance, hue, loggers.CreateLogger<nv_display_controller>());

        store = new profile_store(app_paths.config_file, loggers.CreateLogger<profile_store>());
        config = store.load();
        service = new profile_service(catalog, controller, loggers.CreateLogger<profile_service>());

        // capture BEFORE applying anything, so we can put the display back as we found it
        baseline = color_baseline.capture(catalog, vibrance, hue, loggers.CreateLogger<color_baseline>());

        log.LogInformation(
            "Host ready (nvapi={available}, profiles={profiles})",
            session.is_available, config.profiles.Count);
    }

    public app_config config { get; private set; }

    public bool nvapi_available => session.is_available;

    public string? active_profile_name => service.active_profile_name;

    /// <summary>Applies the configured active profile (called on startup in normal mode).</summary>
    public void apply_active()
    {
        var target = config.find_profile(config.settings.active_profile)
                     ?? config.find_profile(app_config.DEFAULT_PROFILE_NAME);
        if (target is not null)
        {
            service.apply(target);
        }
    }

    /// <summary>Applies a profile and persists it as the new active profile (manual switch).</summary>
    public void apply(profile target)
    {
        service.apply(target);
        config = config with { settings = config.settings with { active_profile = target.name } };
        try
        {
            store.save(config);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not persist active profile");
        }
    }

    /// <summary>
    /// Switches to the next/previous profile relative to the active one (wraps around) and persists
    /// it as the active profile. Used by the global hotkeys.
    /// </summary>
    public void cycle(int direction)
    {
        if (config.profiles.Count == 0)
        {
            return;
        }
        var current = active_profile_name ?? config.settings.active_profile;
        var index = config.profiles.FindIndex(p => string.Equals(p.name, current, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = 0;
        }
        var count = config.profiles.Count;
        var next = ((index + direction) % count + count) % count;
        apply(config.profiles[next]);
    }

    /// <summary>Restores the displays to the state captured at startup.</summary>
    public void restore_baseline() => baseline.restore(vibrance, hue);

    /// <summary>
    /// Hard reset: applies neutral (identity gamma, vibrance 50, hue 0 = NVIDIA defaults) to every
    /// display. Use to undo any color change regardless of profiles/baseline.
    /// </summary>
    public void reset_displays()
    {
        foreach (var display in catalog.get_displays())
        {
            controller.apply(color_settings.neutral, display);
        }
        log.LogInformation("Reset all displays to neutral defaults");
    }

    public bool restore_on_exit => config.settings.restore_on_exit;

    public IReadOnlyList<nv_display> displays => catalog.get_displays();

    /// <summary>Live preview: apply settings to one display without persisting anything.</summary>
    public void preview(color_settings settings, nv_display display) => controller.apply(settings, display);

    /// <summary>Reads the current hardware state of a display (vibrance/hue exact, b/c/g neutral).</summary>
    public color_settings read_current(nv_display display) => controller.read_current(display);

    /// <summary>Re-applies the active profile (e.g. to undo a live preview when settings close).</summary>
    public void reapply_active() => apply_active();

    /// <summary>
    /// Re-asserts whatever profile is currently active on the hardware, without changing any state.
    /// Used after the OS wipes the gamma ramp (standby resume, resolution change, exclusive-
    /// fullscreen exit) — in auto mode this is the rule-driven profile, otherwise the active one.
    /// </summary>
    public void reapply_current()
    {
        var name = active_profile_name ?? config.settings.active_profile;
        var target = config.find_profile(name) ?? config.find_profile(app_config.DEFAULT_PROFILE_NAME);
        if (target is not null)
        {
            service.apply(target);
        }
    }

    /// <summary>
    /// Drops the cached display handles so the next apply re-resolves them. Called when the OS
    /// signals a display change (resolution, monitor hotplug, standby resume).
    /// </summary>
    public void invalidate_displays() => catalog.invalidate();

    /// <summary>
    /// Persists an edited config (profiles + settings). Does NOT re-apply — the settings window
    /// keeps showing its live preview; the active profile is re-applied when the window closes.
    /// </summary>
    public void update_config(app_config edited)
    {
        config = edited.with_default_ensured();
        try
        {
            store.save(config);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not save config");
        }
    }

    public bool autostart_enabled => autostart.is_enabled();

    public void set_autostart(bool enabled) => autostart.set(enabled, Environment.ProcessPath ?? string.Empty);

    public string mode => config.settings.mode;

    public void update_mode(string mode)
    {
        config = config with { settings = config.settings with { mode = mode } };
        try
        {
            store.save(config);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not persist mode");
        }
    }

    /// <summary>
    /// Resolves the profile for a foreground window (app rule, then time schedule, then fallback)
    /// and applies it — but only when it differs from the currently active profile. Transient: the
    /// auto-switch does not overwrite the user's manually chosen active profile in the config.
    /// </summary>
    public void apply_for_foreground(string process_name, string window_title)
    {
        // precedence: a matching app rule, then the time-of-day schedule, then the fallback
        var name = rule_engine.evaluate(config.rules, process_name, window_title)
                   ?? schedule_engine.evaluate(config.schedules, TimeOnly.FromDateTime(DateTime.Now))
                   ?? config.settings.fallback_profile;
        var target = config.find_profile(name) ?? config.find_profile(app_config.DEFAULT_PROFILE_NAME);

        // visible with diagnostic logging on — lets the user discover exact process names
        log.LogDebug(
            "Foreground process='{process}' title='{title}' -> profile '{profile}'",
            process_name, window_title, target?.name);

        if (target is not null && !string.Equals(target.name, active_profile_name, StringComparison.OrdinalIgnoreCase))
        {
            service.apply(target);
        }
    }

    public void Dispose() => session.Dispose();
}
