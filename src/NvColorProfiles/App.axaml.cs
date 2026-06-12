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
using nv_color_profiles.interop;
using nv_color_profiles.localization;
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

        hotkeys = new hotkey_service(loggers.CreateLogger<hotkey_service>());
        hotkeys.triggered += on_hotkey;
        sync_hotkeys();

        apply_startup_mode();
        base.OnFrameworkInitializationCompleted();
    }

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
        return new[]
        {
            new hotkey_service.binding(hotkey_service.hotkey.profile_next, s.hotkey_next.mods, s.hotkey_next.key),
            new hotkey_service.binding(hotkey_service.hotkey.profile_prev, s.hotkey_prev.mods, s.hotkey_prev.key),
            new hotkey_service.binding(hotkey_service.hotkey.toggle_auto, s.hotkey_toggle.mods, s.hotkey_toggle.key),
        };
    }

    // hotkeys fire on their own thread; marshal to the UI thread before touching app state
    private void on_hotkey(hotkey_service.hotkey id) => Dispatcher.UIThread.Post(() =>
    {
        switch (id)
        {
            case hotkey_service.hotkey.profile_next:
                cycle_profile(1);
                break;
            case hotkey_service.hotkey.profile_prev:
                cycle_profile(-1);
                break;
            case hotkey_service.hotkey.toggle_auto:
                toggle_auto(!is_auto);
                break;
        }
    });

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
        var menu = tray.Menu!;
        menu.Items.Clear();

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
        var exit_item = new NativeMenuItem(i18n.t("tray.exit"));
        exit_item.Click += (_, _) => desktop?.Shutdown();
        menu.Items.Add(exit_item);
    }

    private void update_tooltip()
        => tray.ToolTipText = host.nvapi_available
            ? $"NvColorProfiles — {host.active_profile_name ?? "Default"}{(is_auto ? " (Auto)" : string.Empty)}"
            : i18n.t("tray.tooltip_no_gpu");

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
