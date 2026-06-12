using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using nv_color_profiles.app;
using nv_color_profiles.core.display;
using nv_color_profiles.core.profiles;
using nv_color_profiles.interop;
using nv_color_profiles.localization;

namespace nv_color_profiles.views;

// The window groups several independent editors (profiles, rules, schedules, hotkeys, import/export)
// behind one tabbed window. Each editor lives in its own settings_window.*.cs partial; this file
// holds the shared state, construction and the cross-cutting helpers they all use.
public partial class settings_window : Window
{
    private readonly app_host host;
    private readonly IReadOnlyList<nv_display> displays;
    private app_config working;
    private readonly List<string> monitor_keys = new();
    private readonly DispatcherTimer preview_timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private bool preview_dirty;
    private bool loading;
    private bool suppress_fallback_sync;
    private bool suppress_language_sync;

    // set when a language change asks the app to reopen this window so the new language takes hold
    internal bool reopen_for_language { get; private set; }
    internal int current_tab => tab_control.SelectedIndex;

    // parameterless ctor for the XAML designer / loader
    public settings_window()
    {
        host = null!;
        displays = Array.Empty<nv_display>();
        working = app_config.create_default();
        InitializeComponent();
    }

    internal settings_window(app_host host, int initial_tab = 0)
    {
        this.host = host;
        displays = host.displays; // enumerate once — re-querying per slider tick was the lag
        working = clone(host.config);
        InitializeComponent();

        save_button.Click += (_, _) => on_save();
        close_button.Click += (_, _) => Close();
        new_button.Click += async (_, _) => await on_new();
        duplicate_button.Click += (_, _) => on_duplicate();
        rename_button.Click += async (_, _) => await on_rename();
        delete_button.Click += async (_, _) => await on_delete();
        from_current_button.Click += (_, _) => on_from_current();
        reset_monitor_button.Click += (_, _) => on_reset_monitor();
        monitor_combo.SelectionChanged += (_, _) => load_editor();
        profile_list.SelectionChanged += (_, _) => load_editor();
        // fold the fallback pick into working immediately, so it survives a profile add/rename/delete
        // (which rebuild the combo) instead of only being read at save time
        fallback_combo.SelectionChanged += (_, _) =>
        {
            if (!suppress_fallback_sync && fallback_combo.SelectedItem is string name)
            {
                working = working with { settings = working.settings with { fallback_profile = name } };
            }
        };

        rule_new.Click += async (_, _) => await on_rule_new();
        rule_edit.Click += async (_, _) => await on_rule_edit();
        rule_delete.Click += (_, _) => on_rule_delete();
        rule_up.Click += (_, _) => move_rule(-1);
        rule_down.Click += (_, _) => move_rule(1);

        schedule_new.Click += async (_, _) => await on_schedule_new();
        schedule_edit.Click += async (_, _) => await on_schedule_edit();
        schedule_delete.Click += (_, _) => on_schedule_delete();
        delay_slider.ValueChanged += (_, _) => update_delay_label();
        export_button.Click += async (_, _) => await on_export();
        import_button.Click += async (_, _) => await on_import();
        licenses_button.Click += async (_, _) => await licenses_window.show(this);

        hk_next_change.Click += async (_, _) => await rebind(hotkey_service.hotkey.profile_next);
        hk_prev_change.Click += async (_, _) => await rebind(hotkey_service.hotkey.profile_prev);
        hk_toggle_change.Click += async (_, _) => await rebind(hotkey_service.hotkey.toggle_auto);
        hk_next_reset.Click += (_, _) => reset_binding(hotkey_service.hotkey.profile_next);
        hk_prev_reset.Click += (_, _) => reset_binding(hotkey_service.hotkey.profile_prev);
        hk_toggle_reset.Click += (_, _) => reset_binding(hotkey_service.hotkey.toggle_auto);

        language_combo.Items.Add(i18n.t("language.auto"));
        language_combo.Items.Add("Deutsch");
        language_combo.Items.Add("English");
        language_combo.SelectionChanged += (_, _) => on_language_changed();
        delay_value.PointerPressed += on_delay_direct_input;
        // Ctrl+Click on the slider opens direct entry instead of jumping the thumb (tunnel, pre-empts the slider)
        delay_slider.AddHandler(InputElement.PointerPressedEvent, on_delay_slider_pressed, RoutingStrategies.Tunnel, handledEventsToo: true);

        foreach (var s in new[] { brightness_slider, contrast_slider, gamma_slider, vibrance_slider, hue_slider })
        {
            s.ValueChanged += (_, _) => on_slider_changed();
        }
        // Ctrl+Click on the value (or slider) for exact entry; gamma's slider holds the value *100
        wire_direct_entry(brightness_value, brightness_slider, 1);
        wire_direct_entry(contrast_value, contrast_slider, 1);
        wire_direct_entry(gamma_value, gamma_slider, 100);
        wire_direct_entry(vibrance_value, vibrance_slider, 1);
        wire_direct_entry(hue_value, hue_slider, 1);
        preview_timer.Tick += do_preview;
        Closed += (_, _) => preview_timer.Stop();
        // persist edits when the window closes, so nothing is lost by closing without "Save"
        Closing += (_, _) => persist();

        populate_profiles();
        populate_monitors();
        populate_fallback();
        load_general();
        working.rules.Sort((a, b) => a.priority.CompareTo(b.priority));
        refresh_rules();
        refresh_schedules();
        location_label.Text = string.Format(i18n.t("config_location"), nv_color_profiles.core.app_paths.config_file);

        if (profile_list.ItemCount > 0)
        {
            profile_list.SelectedIndex = 0;
        }
        tab_control.SelectedIndex = initial_tab; // restore the tab after a language-change reopen
    }

    private void populate_profiles()
    {
        var index = profile_list.SelectedIndex;
        profile_list.Items.Clear();
        foreach (var p in working.profiles)
        {
            profile_list.Items.Add(p.name);
        }
        if (index >= 0 && index < profile_list.ItemCount)
        {
            profile_list.SelectedIndex = index;
        }
    }

    private void populate_monitors()
    {
        monitor_keys.Clear();
        monitor_combo.Items.Clear();
        monitor_keys.Add(profile.ALL_DISPLAYS);
        monitor_combo.Items.Add(i18n.t("all_displays"));
        foreach (var d in displays)
        {
            monitor_keys.Add(d.display_id.ToString());
            monitor_combo.Items.Add(d.label);
        }
        monitor_combo.SelectedIndex = 0;
    }

    private void populate_fallback()
    {
        // rebuilding clears the selection (and would fire SelectionChanged with junk) — suppress the
        // sync so the user's saved-in-working choice drives the re-selection, not the transient state
        suppress_fallback_sync = true;
        var current = working.settings.fallback_profile;
        fallback_combo.Items.Clear();
        foreach (var p in working.profiles)
        {
            fallback_combo.Items.Add(p.name);
        }
        var idx = working.profiles.FindIndex(p => string.Equals(p.name, current, StringComparison.OrdinalIgnoreCase));
        fallback_combo.SelectedIndex = idx >= 0 ? idx : (fallback_combo.ItemCount > 0 ? 0 : -1);
        suppress_fallback_sync = false;
    }

    private void load_general()
    {
        autostart_check.IsChecked = host.autostart_enabled;
        restore_check.IsChecked = working.settings.restore_on_exit;
        diagnostic_check.IsChecked = working.settings.diagnostic_logging;
        hotkeys_check.IsChecked = working.settings.hotkeys_enabled;
        refresh_hotkey_labels();
        delay_slider.Value = Math.Clamp(working.settings.switch_delay_ms, 0, 60000);
        update_delay_label();

        suppress_language_sync = true;
        language_combo.SelectedIndex = working.settings.language switch { "de" => 1, "en" => 2, _ => 0 };
        suppress_language_sync = false;
    }

    private void on_language_changed()
    {
        if (suppress_language_sync)
        {
            return;
        }
        var code = language_combo.SelectedIndex switch { 1 => "de", 2 => "en", _ => "auto" };
        if (string.Equals(code, working.settings.language, StringComparison.Ordinal))
        {
            return;
        }
        working = working with { settings = working.settings with { language = code } };
        i18n.set_language(i18n.resolve(code));
        // the XAML labels are resolved at load, so reopen the window to apply the new language now
        reopen_for_language = true;
        Close();
    }

    private void update_delay_label() => delay_value.Text = $"{delay_slider.Value / 1000.0:0.0} s";

    private profile? selected_profile()
        => profile_list.SelectedIndex is var i && i >= 0 && i < working.profiles.Count ? working.profiles[i] : null;

    private string selected_key()
    {
        var i = monitor_combo.SelectedIndex;
        return i >= 0 && i < monitor_keys.Count ? monitor_keys[i] : profile.ALL_DISPLAYS;
    }

    private List<string> profile_names() => working.profiles.Select(p => p.name).ToList();

    private static int clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private static app_config clone(app_config c) => c with
    {
        settings = c.settings with { },
        profiles = c.profiles
            .Select(p => p with { displays = new Dictionary<string, color_settings>(p.displays) })
            .ToList(),
        rules = c.rules.ToList(),
        schedules = c.schedules.ToList(),
    };
}
