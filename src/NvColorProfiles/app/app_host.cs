using Microsoft.Extensions.Logging;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using nv_color_profiles.core;
using nv_color_profiles.core.diagnostics;
using nv_color_profiles.core.display;
using nv_color_profiles.core.interop.nvapi;
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
    private readonly nv_api_loader? nvapi_loader;
    private readonly nvapi_gamma_backend? gamma;
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

        // NVAPI when the loader initialises; otherwise gamma is disabled (DVC/hue still work).
        (nvapi_loader, gamma) = build_gamma_backend(loggers);
        controller = new nv_display_controller(gamma, vibrance, hue, loggers.CreateLogger<nv_display_controller>());

        store = new profile_store(app_paths.config_file, loggers.CreateLogger<profile_store>());
        config = store.load();
        service = new profile_service(catalog, controller, loggers.CreateLogger<profile_service>());

        // capture BEFORE applying anything, so we can put the display back as we found it
        baseline = color_baseline.capture(catalog, vibrance, hue, loggers.CreateLogger<color_baseline>());

        log.LogInformation(
            "Host ready (nvapi={available}, gamma={gamma}, profiles={profiles})",
            session.is_available, gamma is null ? "unavailable" : "available", config.profiles.Count);
    }

    private (nv_api_loader? loader, nvapi_gamma_backend? backend) build_gamma_backend(ILoggerFactory loggers)
    {
        if (!session.is_available)
        {
            log.LogInformation("gamma backend unavailable — no NVIDIA session");
            return (null, null);
        }
        try
        {
            var loader = new nv_api_loader();
            return (loader, new nvapi_gamma_backend(loader, loggers.CreateLogger<nvapi_gamma_backend>()));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "gamma backend unavailable — NvAPI init failed");
            return (null, null);
        }
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
    /// it as the active profile. Used by the global hotkeys. Profiles with include_in_cycle=false
    /// are skipped; if the active profile is itself excluded (e.g. applied by a rule), the search
    /// walks the full list in the requested direction until it finds an included candidate.
    /// </summary>
    public void cycle(int direction)
    {
        if (config.profiles.Count == 0)
        {
            return;
        }
        var pool = config.profiles.Where(p => p.include_in_cycle).ToList();
        if (pool.Count == 0)
        {
            log.LogDebug("cycle: no profiles marked include_in_cycle; hotkey is a no-op");
            return;
        }

        var current = active_profile_name ?? config.settings.active_profile;
        var pool_index = pool.FindIndex(p => string.Equals(p.name, current, StringComparison.OrdinalIgnoreCase));
        profile target;
        if (pool_index >= 0)
        {
            var count = pool.Count;
            var next = ((pool_index + direction) % count + count) % count;
            target = pool[next];
        }
        else
        {
            // active profile is not in the cycle pool (excluded by the user, or applied by a rule
            // that referenced an excluded profile) — anchor the search on its position in the full
            // list and walk in the requested direction until we land on an included profile
            var full_index = config.profiles.FindIndex(p => string.Equals(p.name, current, StringComparison.OrdinalIgnoreCase));
            target = find_pool_neighbor(full_index < 0 ? 0 : full_index, direction) ?? pool[0];
        }
        apply(target);
    }

    private profile? find_pool_neighbor(int start_full_index, int direction)
    {
        var count = config.profiles.Count;
        var step = direction >= 0 ? 1 : -1;
        for (var i = 1; i <= count; i++)
        {
            var idx = ((start_full_index + step * i) % count + count) % count;
            var candidate = config.profiles[idx];
            if (candidate.include_in_cycle)
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>Restores the displays to the state captured at startup.</summary>
    public void restore_baseline() => baseline.restore(vibrance, hue, gamma);

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
    /// Re-asserts the currently active profile without state change. Used after the OS wipes the
    /// gamma ramp (standby resume, resolution change, fullscreen-exclusive exit).
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
    /// Resolves the profile for a foreground window (app rule → schedule → fallback) and applies
    /// it if it differs. Transient: does not overwrite the config's active profile.
    /// </summary>
    public void apply_for_foreground(string process_name, string window_title)
    {
        var name = rule_engine.evaluate(config.rules, process_name, window_title)
                   ?? schedule_engine.evaluate(config.schedules, TimeOnly.FromDateTime(DateTime.Now))
                   ?? config.settings.fallback_profile;
        var target = config.find_profile(name) ?? config.find_profile(app_config.DEFAULT_PROFILE_NAME);

        // helps the user discover exact process names when diagnostic logging is on
        log.LogDebug(
            "Foreground process='{process}' title='{title}' -> profile '{profile}'",
            process_name, window_title, target?.name);

        if (target is not null && !string.Equals(target.name, active_profile_name, StringComparison.OrdinalIgnoreCase))
        {
            service.apply(target);
        }
    }

    /// <summary>
    /// Writes a support .zip (config, system, GPU, log tail, NVTweak registry) into
    /// <paramref name="output_dir"/> (defaults to Downloads) and returns the created path.
    /// </summary>
    public string export_diagnostic_bundle(string? output_dir = null, string? output_file = null)
    {
        var inputs = new diagnostic_bundle.inputs(
            app_version: diagnostic_bundle.discover_app_version(),
            config_file_path: app_paths.config_file,
            log_file_path: app_paths.log_file,
            gpu: build_gpu_snapshot());

        diagnostic_bundle.export_result result;
        if (!string.IsNullOrWhiteSpace(output_file))
        {
            result = diagnostic_bundle.export_to_file(inputs, output_file);
        }
        else
        {
            var target_dir = string.IsNullOrWhiteSpace(output_dir)
                ? diagnostic_bundle.default_output_dir()
                : output_dir;
            result = diagnostic_bundle.export(inputs, target_dir);
        }

        log.LogInformation(
            "Diagnostic bundle exported: {path} ({kb} KB)",
            result.zip_path, (result.zip_bytes + 1023) / 1024);
        return result.zip_path;
    }

    private diagnostic_bundle.gpu_snapshot? build_gpu_snapshot()
    {
        if (!session.is_available)
        {
            return null;
        }

        var driver_version = format_driver(NVIDIA.DriverVersion);
        var driver_branch = NVIDIA.DriverBranchVersion ?? string.Empty;

        var gpu_names = new List<string>();
        try
        {
            foreach (var gpu in PhysicalGPU.GetPhysicalGPUs())
            {
                gpu_names.Add(gpu.FullName ?? "(unnamed GPU)");
            }
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Could not enumerate physical GPUs for diagnostic bundle");
        }

        var display_entries = new List<diagnostic_bundle.display_entry>();
        foreach (var d in catalog.get_displays())
        {
            uint? luid = null;
            if (nvapi_loader is not null)
            {
                try
                {
                    luid = nv_display_enum.get_luid_for_display(nvapi_loader, d.display_id);
                }
                catch (Exception ex)
                {
                    log.LogDebug(ex, "LUID lookup failed for display 0x{id:X8}", d.display_id);
                }
            }
            display_entries.Add(new diagnostic_bundle.display_entry(d.display_id, luid, d.gdi_name));
        }

        return new diagnostic_bundle.gpu_snapshot(driver_version, driver_branch, gpu_names, display_entries);
    }

    private static string format_driver(uint version) => $"{version / 100}.{version % 100:D2}";

    /// <summary>True when the NVAPI gamma backend is wired up; false on non-NVIDIA systems or when NvAPI init failed.</summary>
    public bool gamma_available => gamma is not null;

    public void Dispose()
    {
        nvapi_loader?.Dispose();
        session.Dispose();
    }
}
