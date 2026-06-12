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

    private async Task rebind(hotkey_service.hotkey id)
    {
        var result = await hotkey_capture.capture(this, binding_for(id));
        if (result is not null)
        {
            set_binding(id, result);
            refresh_hotkey_labels();
        }
    }

    private void reset_binding(hotkey_service.hotkey id)
    {
        var defaults = new app_settings();
        var fresh = id switch
        {
            hotkey_service.hotkey.profile_next => defaults.hotkey_next,
            hotkey_service.hotkey.profile_prev => defaults.hotkey_prev,
            _ => defaults.hotkey_toggle,
        };
        set_binding(id, fresh);
        refresh_hotkey_labels();
    }

    private hotkey_binding binding_for(hotkey_service.hotkey id) => id switch
    {
        hotkey_service.hotkey.profile_next => working.settings.hotkey_next,
        hotkey_service.hotkey.profile_prev => working.settings.hotkey_prev,
        _ => working.settings.hotkey_toggle,
    };

    private void set_binding(hotkey_service.hotkey id, hotkey_binding b)
    {
        working = working with
        {
            settings = id switch
            {
                hotkey_service.hotkey.profile_next => working.settings with { hotkey_next = b },
                hotkey_service.hotkey.profile_prev => working.settings with { hotkey_prev = b },
                _ => working.settings with { hotkey_toggle = b },
            },
        };
    }
}
