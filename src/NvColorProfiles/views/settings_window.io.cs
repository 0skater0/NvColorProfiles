using Avalonia.Platform.Storage;
using Avalonia.Threading;
using nv_color_profiles.core.display;
using nv_color_profiles.core.profiles;
using nv_color_profiles.localization;

namespace nv_color_profiles.views;

// Persisting the edited config (save) and the JSON import/export of profiles, rules and schedules.
public partial class settings_window
{
    // folds the current UI state (rule order, fallback, general toggles) back into `working`
    private app_config collect()
    {
        for (var i = 0; i < working.rules.Count; i++)
        {
            working.rules[i] = working.rules[i] with { priority = i };
        }

        var fallback = fallback_combo.SelectedIndex >= 0 && fallback_combo.SelectedIndex < working.profiles.Count
            ? working.profiles[fallback_combo.SelectedIndex].name
            : app_config.DEFAULT_PROFILE_NAME;

        working = working with
        {
            settings = working.settings with
            {
                restore_on_exit = restore_check.IsChecked ?? true,
                diagnostic_logging = diagnostic_check.IsChecked ?? false,
                autostart = autostart_check.IsChecked ?? false,
                hotkeys_enabled = hotkeys_check.IsChecked ?? true,
                fallback_profile = fallback,
                switch_delay_ms = (int)delay_slider.Value,
                mode = host.mode, // the tray owns the mode
            },
        };
        return working;
    }

    // writes the current UI state to disk (shared by the Save button and the close handler)
    private void persist()
    {
        host.set_autostart(autostart_check.IsChecked ?? false);
        host.update_config(collect());
    }

    private void on_save()
    {
        persist();

        // keep the window open (so calibration continues) — just confirm briefly
        save_button.Content = i18n.t("saved");
        DispatcherTimer.RunOnce(() => save_button.Content = i18n.t("save"), TimeSpan.FromSeconds(1.5));
    }

    private async Task on_export()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = i18n.t("export.title"),
            SuggestedFileName = "nvcolorprofiles-profile.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });
        if (file is null)
        {
            return;
        }
        try
        {
            var json = profile_store.to_json(collect());
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            io_status.Text = string.Format(i18n.t("export.done"), file.Name);
        }
        catch (Exception ex)
        {
            io_status.Text = string.Format(i18n.t("export.failed"), ex.Message);
        }
    }

    private async Task on_import()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = i18n.t("import.title"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });
        var file = files.Count > 0 ? files[0] : null;
        if (file is null)
        {
            return;
        }

        app_config? imported;
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            imported = profile_store.from_json(await reader.ReadToEndAsync());
        }
        catch (Exception ex)
        {
            io_status.Text = string.Format(i18n.t("import.failed"), ex.Message);
            return;
        }

        if (imported is null)
        {
            io_status.Text = i18n.t("import.invalid");
            return;
        }

        // take the imported profiles + rules (and the profile-bound fallback/delay); keep local
        // machine settings like autostart, restore-on-exit and diagnostics untouched
        working = working with
        {
            profiles = imported.profiles
                .Select(p => p with { displays = new Dictionary<string, color_settings>(p.displays) })
                .ToList(),
            rules = imported.rules.ToList(),
            schedules = imported.schedules.ToList(),
            settings = working.settings with
            {
                fallback_profile = imported.settings.fallback_profile,
                switch_delay_ms = imported.settings.switch_delay_ms,
            },
        };
        working.rules.Sort((a, b) => a.priority.CompareTo(b.priority));

        // the previously active profile may not exist in the imported set — point it at a profile
        // that does, so apply doesn't silently drop to Default without the config reflecting it
        var active_reset = working.find_profile(working.settings.active_profile) is null;
        if (active_reset)
        {
            var safe_active = working.find_profile(working.settings.fallback_profile)?.name
                              ?? app_config.DEFAULT_PROFILE_NAME;
            working = working with { settings = working.settings with { active_profile = safe_active } };
        }

        populate_profiles();
        populate_fallback();
        refresh_rules();
        refresh_schedules();
        load_general();
        profile_list.SelectedIndex = working.profiles.Count > 0 ? 0 : -1;

        host.update_config(collect());
        io_status.Text =
            string.Format(i18n.t("import.done"), working.profiles.Count, working.rules.Count, working.schedules.Count)
            + (active_reset ? i18n.t("import.active_reset") : string.Empty);
    }
}
