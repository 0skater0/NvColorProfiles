using nv_color_profiles.core.profiles;
using nv_color_profiles.interop;
using nv_color_profiles.localization;

namespace nv_color_profiles.views;

// General tab: the global-hotkey rebind rows (the labels + "change"/"reset" actions).
public partial class settings_window
{
    private void refresh_hotkey_labels()
    {
        hk_next_label.Text = working.settings.hotkey_next.display_name(i18n.is_english);
        hk_prev_label.Text = working.settings.hotkey_prev.display_name(i18n.is_english);
        hk_toggle_label.Text = working.settings.hotkey_toggle.display_name(i18n.is_english);
    }

    private async Task rebind(hotkey_service.hotkey_kind kind)
    {
        var result = await hotkey_capture.capture(this, binding_for(kind));
        if (result is not null)
        {
            set_binding(kind, result);
            refresh_hotkey_labels();
            refresh_profile_hotkey_row(); // conflict text may need to update
            refresh_profile_row_labels();
        }
    }

    private void reset_binding(hotkey_service.hotkey_kind kind)
    {
        var defaults = new app_settings();
        var fresh = kind switch
        {
            hotkey_service.hotkey_kind.profile_next => defaults.hotkey_next,
            hotkey_service.hotkey_kind.profile_prev => defaults.hotkey_prev,
            _ => defaults.hotkey_toggle,
        };
        set_binding(kind, fresh);
        refresh_hotkey_labels();
        refresh_profile_hotkey_row();
        refresh_profile_row_labels();
    }

    private hotkey_binding binding_for(hotkey_service.hotkey_kind kind) => kind switch
    {
        hotkey_service.hotkey_kind.profile_next => working.settings.hotkey_next,
        hotkey_service.hotkey_kind.profile_prev => working.settings.hotkey_prev,
        _ => working.settings.hotkey_toggle,
    };

    private void set_binding(hotkey_service.hotkey_kind kind, hotkey_binding b)
    {
        working = working with
        {
            settings = kind switch
            {
                hotkey_service.hotkey_kind.profile_next => working.settings with { hotkey_next = b },
                hotkey_service.hotkey_kind.profile_prev => working.settings with { hotkey_prev = b },
                _ => working.settings with { hotkey_toggle = b },
            },
        };
    }
}
