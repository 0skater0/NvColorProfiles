using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using nv_color_profiles.core.display;
using nv_color_profiles.core.profiles;
using nv_color_profiles.localization;

namespace nv_color_profiles.views;

// Profile tab: the per-display sliders, live hardware preview, and profile CRUD.
public partial class settings_window
{
    private void load_editor()
    {
        loading = true;
        var p = selected_profile();
        var editable = p is { builtin: false } && host.nvapi_available;

        var s = p?.settings_for(selected_key()) ?? color_settings.neutral;
        brightness_slider.Value = clamp(s.brightness, 0, 100);
        contrast_slider.Value = clamp(s.contrast, 0, 100);
        gamma_slider.Value = clamp((int)Math.Round(s.gamma * 100), 40, 280);
        vibrance_slider.Value = clamp(s.vibrance, 0, 100);
        hue_slider.Value = clamp(s.hue, 0, 359);
        update_value_labels();

        foreach (var slider in new[] { brightness_slider, contrast_slider, gamma_slider, vibrance_slider, hue_slider })
        {
            slider.IsEnabled = editable;
        }
        monitor_combo.IsEnabled = editable;
        from_current_button.IsEnabled = editable;
        rename_button.IsEnabled = p is { builtin: false };
        delete_button.IsEnabled = p is { builtin: false } && working.profiles.Count > 1;
        duplicate_button.IsEnabled = p is not null;
        hint_label.Text = p is { builtin: true }
            ? i18n.t("hint.builtin")
            : host.nvapi_available ? i18n.t("hint.editable") : i18n.t("hint.no_gpu");

        refresh_monitor_hint();
        loading = false;
    }

    // explains the current monitor scope (base value vs a monitor's own values) and toggles the
    // "reset monitor" button, so per-display overrides are visible instead of silently hidden
    private void refresh_monitor_hint()
    {
        var p = selected_profile();
        var key = selected_key();
        var editable = p is { builtin: false } && host.nvapi_available;

        if (p is null || key == profile.ALL_DISPLAYS)
        {
            var custom = displays
                .Where(d => p is not null && p.displays.ContainsKey(d.display_id.ToString()))
                .Select(d => d.label)
                .ToList();
            monitor_hint.Text = custom.Count == 0
                ? i18n.t("monitor.all")
                : i18n.t("monitor.all") + " " + string.Format(i18n.t("monitor.all_custom"), string.Join(", ", custom));
            reset_monitor_button.IsEnabled = false;
            return;
        }

        var label = monitor_combo.SelectedItem as string ?? key;
        var has_own = p.displays.ContainsKey(key);
        monitor_hint.Text = string.Format(i18n.t(has_own ? "monitor.own" : "monitor.inherits"), label);
        reset_monitor_button.IsEnabled = editable && has_own;
    }

    // removes the selected monitor's own values so it follows the All-displays base again
    private void on_reset_monitor()
    {
        var p = selected_profile();
        var key = selected_key();
        if (p is null || p.builtin || key == profile.ALL_DISPLAYS)
        {
            return;
        }
        if (p.displays.Remove(key))
        {
            load_editor();   // reloads the inherited base value into the sliders
            apply_preview(); // and shows it on that monitor right away
        }
    }

    private void on_slider_changed()
    {
        update_value_labels();
        if (loading)
        {
            return;
        }
        var p = selected_profile();
        if (p is null || p.builtin)
        {
            return;
        }
        p.displays[selected_key()] = read_sliders(); // cheap in-memory update, immediate
        preview_dirty = true;

        // throttle the hardware preview: apply now on the first move, then ~25 fps while dragging
        if (!preview_timer.IsEnabled)
        {
            preview_dirty = false;
            apply_preview();
            preview_timer.Start();
        }
        refresh_monitor_hint(); // a first edit on a specific monitor just gave it its own values
    }

    private void do_preview(object? sender, EventArgs e)
    {
        if (preview_dirty)
        {
            preview_dirty = false;
            apply_preview();
        }
        else
        {
            preview_timer.Stop(); // idle — stop ticking until the next change
        }
    }

    private void apply_preview()
    {
        var p = selected_profile();
        if (p is null || p.builtin)
        {
            return;
        }
        var key = selected_key();
        var s = read_sliders();

        if (key == profile.ALL_DISPLAYS)
        {
            foreach (var d in displays)
            {
                host.preview(s, d);
            }
        }
        else
        {
            var d = displays.FirstOrDefault(x => x.display_id.ToString() == key);
            if (d is not null)
            {
                host.preview(s, d);
            }
        }
    }

    private color_settings read_sliders() => new(
        (int)brightness_slider.Value,
        (int)contrast_slider.Value,
        Math.Round(gamma_slider.Value / 100.0, 2),
        (int)vibrance_slider.Value,
        (int)hue_slider.Value);

    private void update_value_labels()
    {
        brightness_value.Text = $"{(int)brightness_slider.Value} %";
        contrast_value.Text = $"{(int)contrast_slider.Value} %";
        gamma_value.Text = (gamma_slider.Value / 100.0).ToString("0.00");
        vibrance_value.Text = $"{(int)vibrance_slider.Value} %";
        hue_value.Text = $"{(int)hue_slider.Value}°";
    }

    // Ctrl+Click on a value label (or the slider) opens a text field for the exact number. `scale`
    // maps the displayed value to the slider value — 1 for percentages/degrees, 100 for gamma
    // (slider holds gamma*100 while the label shows 0.40..2.80).
    private void wire_direct_entry(TextBlock label, Slider slider, double scale)
    {
        label.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(label, i18n.t("direct_entry_tip"));
        label.PointerPressed += async (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                await prompt_slider(slider, scale);
            }
        };
        // Ctrl+Click on the slider opens entry instead of jumping the thumb (tunnel, pre-empts it)
        slider.AddHandler(
            InputElement.PointerPressedEvent,
            async (_, e) =>
            {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    return;
                }
                e.Handled = true;
                await prompt_slider(slider, scale);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private async Task prompt_slider(Slider slider, double scale)
    {
        var current = (slider.Value / scale).ToString("0.##", CultureInfo.InvariantCulture);
        var input = await text_prompt.ask(this, i18n.t("enter_value"), current);
        if (input is null)
        {
            return;
        }
        if (double.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            slider.Value = Math.Clamp(v * scale, slider.Minimum, slider.Maximum);
        }
    }

    private async Task on_new()
    {
        var name = await text_prompt.ask(this, i18n.t("profile.new_default"), unique_name(i18n.t("profile.new_default")));
        if (name is not null)
        {
            add_profile(profile.uniform(unique_name(name), color_settings.neutral));
        }
    }

    private void on_duplicate()
    {
        var p = selected_profile();
        if (p is null)
        {
            return;
        }
        add_profile(new profile
        {
            name = unique_name(p.name + i18n.t("profile.copy_suffix")),
            displays = new Dictionary<string, color_settings>(p.displays),
        });
    }

    private async Task on_rename()
    {
        var p = selected_profile();
        if (p is null || p.builtin)
        {
            return;
        }
        var name = await text_prompt.ask(this, i18n.t("profile.rename_title"), p.name);
        if (name is null || string.Equals(name, p.name, StringComparison.Ordinal))
        {
            return;
        }
        var renamed = p with { name = unique_name(name) };
        working.profiles[working.profiles.IndexOf(p)] = renamed;
        if (string.Equals(working.settings.active_profile, p.name, StringComparison.OrdinalIgnoreCase))
        {
            working = working with { settings = working.settings with { active_profile = renamed.name } };
        }
        populate_profiles();
        populate_fallback();
        profile_list.SelectedIndex = working.profiles.IndexOf(renamed);
    }

    private async Task on_delete()
    {
        var p = selected_profile();
        if (p is null || p.builtin || working.profiles.Count <= 1)
        {
            return;
        }
        var confirmed = await confirm_dialog.ask(
            this,
            i18n.t("profile.delete_title"),
            string.Format(i18n.t("profile.delete_confirm"), p.name),
            i18n.t("delete"),
            i18n.t("cancel"));
        if (!confirmed)
        {
            return;
        }
        working.profiles.Remove(p);
        populate_profiles();
        populate_fallback();
        profile_list.SelectedIndex = 0;
    }

    private void on_from_current()
    {
        var p = selected_profile();
        if (p is null || p.builtin)
        {
            return;
        }
        var key = selected_key();
        var d = key == profile.ALL_DISPLAYS
            ? displays.FirstOrDefault()
            : displays.FirstOrDefault(x => x.display_id.ToString() == key);
        if (d is null)
        {
            return;
        }
        var current = host.read_current(d);
        loading = true;
        brightness_slider.Value = clamp(current.brightness, 0, 100);
        contrast_slider.Value = clamp(current.contrast, 0, 100);
        gamma_slider.Value = clamp((int)Math.Round(current.gamma * 100), 40, 280);
        vibrance_slider.Value = clamp(current.vibrance, 0, 100);
        hue_slider.Value = clamp(current.hue, 0, 359);
        update_value_labels();
        loading = false;
        on_slider_changed();
    }

    private void add_profile(profile p)
    {
        working.profiles.Add(p);
        populate_profiles();
        populate_fallback();
        profile_list.SelectedIndex = working.profiles.IndexOf(p);
    }

    private string unique_name(string desired)
    {
        var name = desired.Trim();
        if (name.Length == 0)
        {
            name = i18n.t("profile.new_default");
        }
        var candidate = name;
        var n = 2;
        while (working.profiles.Any(p => string.Equals(p.name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{name} {n++}";
        }
        return candidate;
    }
}
