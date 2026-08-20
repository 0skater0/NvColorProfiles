using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Extensions.Logging.Abstractions;
using nv_color_profiles.app;
using nv_color_profiles.core;
using nv_color_profiles.core.diagnostics;
using nv_color_profiles.core.profiles;
using nv_color_profiles.core.updates;
using nv_color_profiles.interop;
using nv_color_profiles.localization;
using nv_color_profiles.updates;
using nv_color_profiles.views;

namespace nv_color_profiles;

public partial class nv_app : Application
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    private ILoggerFactory loggers = null!;
    private ILogger log = null!;
    private app_host host = null!;
    private foreground_watcher? watcher;
    private hotkey_service? hotkeys;
    private DispatcherTimer? schedule_timer;
    private string last_process = string.Empty;
    private string last_title = string.Empty;
    private TrayIcon tray = null!;
    private IClassicDesktopStyleApplicationLifetime? desktop;
    private settings_window? settings;
    private update_checker? updater;
    private DispatcherTimer? update_timer;
    // populated by the periodic checker and used by the tray tooltip/menu; null = no update seen yet
    private string? update_available_version;
    private string? update_available_url;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var level = peek_diagnostic_logging() ? LogLevel.Debug : LogLevel.Information;
        loggers = log_setup.create_factory(level);
        log = loggers.CreateLogger("app");
        host = new app_host(loggers);

        // set the UI language before any window or the tray menu is built
        i18n.set_language(i18n.resolve(host.config.settings.language));

        // crash safety: log + restore the captured baseline on any unhandled exception
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.LogError(e.ExceptionObject as Exception, "Unhandled exception");
            try { host.restore_baseline(); } catch { /* best-effort */ }
        };

        tray = new TrayIcon { Icon = load_icon(), ToolTipText = "NvColorProfiles", IsVisible = true, Menu = new NativeMenu() };
        TrayIcon.SetIcons(this, new TrayIcons { tray });

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            desktop = lifetime;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => cleanup();
        }

        // the OS wipes the gamma ramp on these events; re-assert the active profile afterwards
        SystemEvents.DisplaySettingsChanged += on_system_display_changed;
        SystemEvents.PowerModeChanged += on_system_power_changed;

        hotkeys = new hotkey_service(loggers.CreateLogger<hotkey_service>(), loggers);
        hotkeys.triggered += on_hotkey;
        sync_hotkeys();

        apply_startup_mode();
        // dispatch onto the UI loop's next tick so Show() runs after framework startup completes
        Dispatcher.UIThread.Post(() => _ = start_update_flow_async());
        base.OnFrameworkInitializationCompleted();
    }

    // Runs the first-run modal (once), then arms the periodic 24h check. Never awaited by the
    // framework hook so a slow prompt or a network hiccup can't stall the tray coming up.
    private async Task start_update_flow_async()
    {
        try
        {
            if (!host.config.settings.update_check_prompted)
            {
                await show_first_run_modal_async();
            }
            arm_update_timer();
        }
        catch (Exception ex)
        {
            log.LogInformation(ex, "Update flow bootstrap failed");
        }
    }

    private async Task show_first_run_modal_async()
    {
        try
        {
            var opted_in = await first_run_dialog.ask();
            host.update_config(host.config with
            {
                settings = host.config.settings with
                {
                    update_check_enabled = opted_in,
                    update_check_prompted = true,
                },
            });
        }
        catch (Exception ex)
        {
            log.LogInformation(ex, "First-run modal failed; falling back to opt-in default");
            host.update_config(host.config with
            {
                settings = host.config.settings with { update_check_prompted = true },
            });
        }
    }

    // 5-minute delay before the first check keeps startup responsive; the DispatcherTimer then fires
    // hourly and asks should_check(...) whether the 24h cadence is due.
    private void arm_update_timer()
    {
        if (update_timer is not null)
        {
            return;
        }
        update_timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(60) };
        update_timer.Tick += async (_, _) => await maybe_run_periodic_check_async();
        DispatcherTimer.RunOnce(async () =>
        {
            await maybe_run_periodic_check_async();
            update_timer.Start();
        }, TimeSpan.FromMinutes(5));
    }

    private async Task maybe_run_periodic_check_async()
    {
        if (!host.config.settings.update_check_enabled || !host.config.settings.update_check_prompted)
        {
            return;
        }
        if (!update_checker.should_check(host.config.settings.last_update_check_at, DateTime.UtcNow))
        {
            return;
        }
        var result = await run_update_check_async();
        if (result.is_newer && result.latest_version is not null && result.latest_url is not null)
        {
            var already_seen = string.Equals(host.config.settings.latest_seen_version, result.latest_version, OIC);
            update_available_version = result.latest_version;
            update_available_url = result.latest_url;
            update_tooltip();
            rebuild_menu();
            // only surface the toast once per new release; the badge stays until acknowledged
            if (!already_seen)
            {
                var body = string.Format(i18n.t("updates.toast_body"), result.latest_version);
                toast_notifier.show_update_available(i18n.t("updates.toast_title"), body, result.latest_url, log);
            }
            host.update_config(host.config with
            {
                settings = host.config.settings with { latest_seen_version = result.latest_version },
            });
        }
    }

    private async Task<update_check_result> run_update_check_async()
    {
        updater ??= new update_checker(current_app_version(), loggers.CreateLogger<update_checker>());
        var result = await updater.check_async(current_app_version());
        host.update_config(host.config with
        {
            settings = host.config.settings with { last_update_check_at = result.checked_at },
        });
        return result;
    }

    private async Task<(string message, string? release_url)> run_manual_update_check_async()
    {
        var result = await run_update_check_async();
        if (result.error is not null)
        {
            return (string.Format(i18n.t("updates.error"), result.error), null);
        }
        if (result.is_newer && result.latest_version is not null && result.latest_url is not null)
        {
            update_available_version = result.latest_version;
            update_available_url = result.latest_url;
            update_tooltip();
            rebuild_menu();
            host.update_config(host.config with
            {
                settings = host.config.settings with { latest_seen_version = result.latest_version },
            });
            return (string.Format(i18n.t("updates.available"), result.latest_version), result.latest_url);
        }
        // no newer release; drop any stale badge (e.g. server rolled a release back)
        update_available_version = null;
        update_available_url = null;
        update_tooltip();
        rebuild_menu();
        return (i18n.t("updates.up_to_date"), null);
    }

    private static string current_app_version() => diagnostic_bundle.discover_app_version();

    private bool is_auto => string.Equals(host.mode, "auto", OIC);

    private void apply_startup_mode()
    {
        if (host.nvapi_available && is_auto)
        {
            toggle_auto(true);
        }
        else
        {
            host.apply_active();
        }
        update_tooltip();
        rebuild_menu();
    }

    private void toggle_auto(bool on)
    {
        if (on)
        {
            host.update_mode("auto");
            if (watcher is null)
            {
                watcher = new foreground_watcher();
                watcher.changed += on_foreground;
            }
            watcher.set_delay(host.config.settings.switch_delay_ms);
            watcher.start();
            ensure_schedule_timer();
            schedule_timer!.Start();
        }
        else
        {
            host.update_mode("manual");
            watcher?.stop();
            schedule_timer?.Stop();
            host.apply_active();
        }
        update_tooltip();
        rebuild_menu();
    }

    private void on_foreground(string process, string title)
    {
        last_process = process;
        last_title = title;
        host.apply_for_foreground(process, title);
        update_tooltip();
        rebuild_menu();
    }

    // re-evaluates the active context periodically so a time-schedule boundary takes effect even
    // when the foreground window hasn't changed
    private void ensure_schedule_timer()
    {
        if (schedule_timer is not null)
        {
            return;
        }
        schedule_timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        schedule_timer.Tick += (_, _) =>
        {
            if (is_auto)
            {
                on_foreground(last_process, last_title);
            }
        };
    }

    private void sync_hotkeys()
    {
        if (hotkeys is null)
        {
            return;
        }
        // stop first so a changed binding re-registers cleanly (start/set are no-ops if disabled)
        hotkeys.stop();
        if (host.config.settings.hotkeys_enabled)
        {
            hotkeys.set_bindings(current_bindings());
            hotkeys.start();
        }
    }

    private hotkey_service.binding[] current_bindings()
    {
        var s = host.config.settings;
        var result = new List<hotkey_service.binding>
        {
            new(1, hotkey_service.hotkey_kind.profile_next, null, s.hotkey_next.mods, s.hotkey_next.key, s.hotkey_next.mouse_button),
            new(2, hotkey_service.hotkey_kind.profile_prev, null, s.hotkey_prev.mods, s.hotkey_prev.key, s.hotkey_prev.mouse_button),
            new(3, hotkey_service.hotkey_kind.toggle_auto, null, s.hotkey_toggle.mods, s.hotkey_toggle.key, s.hotkey_toggle.mouse_button),
        };
        // per-profile direct hotkeys start at 100 to leave a gap for future global actions
        var next_id = 100;
        foreach (var profile in host.config.profiles)
        {
            var hk = profile.hotkey;
            if (hk is { is_set: true })
            {
                result.Add(new hotkey_service.binding(next_id++, hotkey_service.hotkey_kind.apply_profile, profile.name, hk.mods, hk.key, hk.mouse_button));
            }
        }
        return result.ToArray();
    }

    // hotkeys fire on their own thread; marshal to the UI thread before touching app state
    private void on_hotkey(hotkey_service.hotkey_kind kind, string? payload) => Dispatcher.UIThread.Post(() =>
    {
        switch (kind)
        {
            case hotkey_service.hotkey_kind.profile_next:
                cycle_profile(1);
                break;
            case hotkey_service.hotkey_kind.profile_prev:
                cycle_profile(-1);
                break;
            case hotkey_service.hotkey_kind.toggle_auto:
                toggle_auto(!is_auto);
                break;
            case hotkey_service.hotkey_kind.apply_profile:
                apply_profile_hotkey(payload);
                break;
        }
    });

    private void apply_profile_hotkey(string? name)
    {
        if (!host.nvapi_available || string.IsNullOrEmpty(name))
        {
            return;
        }
        var target = host.config.find_profile(name);
        if (target is null)
        {
            return; // profile was deleted between binding and press; silent no-op
        }
        if (is_auto)
        {
            toggle_auto(false); // a direct pick is a manual selection
        }
        host.apply(target);
        update_tooltip();
        rebuild_menu();
    }

    private void cycle_profile(int direction)
    {
        if (!host.nvapi_available)
        {
            return;
        }
        if (is_auto)
        {
            toggle_auto(false); // cycling is a manual selection
        }
        host.cycle(direction);
        update_tooltip();
        rebuild_menu();
    }

    private void on_system_display_changed(object? sender, EventArgs e) => schedule_reapply();

    private void on_system_power_changed(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            schedule_reapply();
        }
    }

    // SystemEvents fire on their own thread; marshal to the UI thread and wait a moment so Windows
    // has finished restoring its own gamma defaults before we re-assert ours.
    private void schedule_reapply() => Dispatcher.UIThread.Post(() =>
        DispatcherTimer.RunOnce(
            () =>
            {
                try
                {
                    host.invalidate_displays(); // topology may have changed; drop stale NvAPI handles
                    host.reapply_current();
                    update_tooltip();
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Re-apply after display/power change failed");
                }
            },
            TimeSpan.FromMilliseconds(750)));

    private void rebuild_menu()
    {
        // Build a fresh NativeMenu and assign it, instead of mutating tray.Menu.Items.
        // Windows Shell caches the tray context menu's native handle across an Items.Clear() +
        // Add() cycle, so removed profiles (e.g. after Delete + autosave-on-close) kept
        // appearing in the tray menu until the app was restarted. Reassigning the menu forces
        // a full rebuild of the native handle.
        var menu = new NativeMenu();

        if (!host.nvapi_available)
        {
            menu.Items.Add(new NativeMenuItem(i18n.t("tray.no_gpu")) { IsEnabled = false });
        }
        else
        {
            foreach (var profile in host.config.profiles)
            {
                var target = profile;
                var is_active = string.Equals(profile.name, host.active_profile_name, OIC);
                var item = new NativeMenuItem(profile.name + (is_active ? "  ✓" : string.Empty))
                {
                    IsChecked = is_active,
                };
                item.Click += (_, _) =>
                {
                    if (is_auto)
                    {
                        toggle_auto(false); // manual selection takes over
                    }
                    host.apply(target);
                    update_tooltip();
                    rebuild_menu();
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new NativeMenuItemSeparator());
            // IsChecked alone draws no glyph in the tray menu, so show the on-state in the label
            var auto_item = new NativeMenuItem(i18n.t("tray.auto") + (is_auto ? "  ✓" : string.Empty))
            {
                IsChecked = is_auto,
            };
            auto_item.Click += (_, _) => toggle_auto(!is_auto);
            menu.Items.Add(auto_item);
        }

        menu.Items.Add(new NativeMenuItemSeparator());
        var reset_item = new NativeMenuItem(i18n.t("tray.reset")) { IsEnabled = host.nvapi_available };
        reset_item.Click += (_, _) =>
        {
            host.reset_displays();
            update_tooltip();
        };
        menu.Items.Add(reset_item);
        var settings_item = new NativeMenuItem(i18n.t("tray.settings"));
        settings_item.Click += (_, _) => open_settings();
        menu.Items.Add(settings_item);
        if (update_available_version is not null && update_available_url is not null)
        {
            var update_item = new NativeMenuItem(string.Format(i18n.t("updates.available"), update_available_version));
            update_item.Click += (_, _) => open_release_page();
            menu.Items.Add(update_item);
        }
        var exit_item = new NativeMenuItem(i18n.t("tray.exit"));
        exit_item.Click += (_, _) => desktop?.Shutdown();
        menu.Items.Add(exit_item);

        tray.Menu = menu;
    }

    private void open_release_page()
    {
        if (string.IsNullOrWhiteSpace(update_available_url))
        {
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(update_available_url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            log.LogInformation(ex, "Opening release URL from tray failed");
        }
    }

    private void update_tooltip()
    {
        var suffix = update_available_version is null ? string.Empty : i18n.t("tray.tooltip_update_suffix");
        tray.ToolTipText = host.nvapi_available
            ? $"NvColorProfiles — {host.active_profile_name ?? "Default"}{(is_auto ? " (Auto)" : string.Empty)}{suffix}"
            : i18n.t("tray.tooltip_no_gpu") + suffix;
    }

    private void open_settings(int tab = 0)
    {
        try
        {
            if (settings is not null)
            {
                settings.Activate();
                return;
            }
            settings = new settings_window(host, tab);
            settings.manual_update_check = run_manual_update_check_async;
            settings.saved += () =>
            {
                // Save-click applies hotkey/tooltip/menu changes immediately (window stays open)
                sync_hotkeys();
                update_tooltip();
                rebuild_menu();
            };
            settings.Closed += (_, _) =>
            {
                // a language change closes the window and asks to reopen it in the new language
                var reopen = settings is { reopen_for_language: true };
                var reopen_tab = settings?.current_tab ?? 0;
                settings = null;
                host.reapply_active(); // undo any live preview
                watcher?.set_delay(host.config.settings.switch_delay_ms); // delay may have changed
                sync_hotkeys(); // hotkey toggle may have changed
                update_tooltip();
                rebuild_menu();
                if (reopen)
                {
                    open_settings(reopen_tab);
                }
            };
            settings.Show();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Opening the settings window failed");
        }
    }

    private void cleanup()
    {
        SystemEvents.DisplaySettingsChanged -= on_system_display_changed;
        SystemEvents.PowerModeChanged -= on_system_power_changed;
        if (host.restore_on_exit)
        {
            host.restore_baseline();
        }
        if (hotkeys is not null)
        {
            hotkeys.triggered -= on_hotkey;
            hotkeys.Dispose();
        }
        schedule_timer?.Stop();
        update_timer?.Stop();
        updater?.dispose();
        watcher?.Dispose();
        host.Dispose();
    }

    // read just the diagnostic flag before the real logger factory exists
    private static bool peek_diagnostic_logging()
    {
        try
        {
            return new profile_store(app_paths.config_file, NullLogger<profile_store>.Instance)
                .load().settings.diagnostic_logging;
        }
        catch
        {
            return false;
        }
    }

    private static WindowIcon load_icon()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("nvcolorprofiles.ico");
        return new WindowIcon(stream!);
    }
}
