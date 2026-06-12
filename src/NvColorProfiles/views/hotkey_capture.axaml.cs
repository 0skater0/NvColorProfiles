using Avalonia.Controls;
using Avalonia.Input;
using nv_color_profiles.core.profiles;
using nv_color_profiles.interop;
using nv_color_profiles.localization;

namespace nv_color_profiles.views;

public partial class hotkey_capture : Window
{
    private hotkey_binding? captured;

    public hotkey_capture()
    {
        InitializeComponent();
    }

    private hotkey_capture(hotkey_binding? current) : this()
    {
        if (current is { is_set: true })
        {
            preview.Text = current.display_name(i18n.is_english);
        }
        ok_button.Click += (_, _) => Close(captured);
        cancel_button.Click += (_, _) => Close(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // let the default/cancel buttons handle Enter/Escape
        if (e.Key is Key.Escape or Key.Return)
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true; // swallow everything else (incl. Tab) so it can't move focus

        if (hotkey_keys.is_modifier_key(e.Key))
        {
            return; // a bare modifier — wait for the actual key
        }

        var mods = hotkey_keys.to_win_mods(e.KeyModifiers);
        if (!hotkey_keys.has_required_modifier(mods))
        {
            hint.Text = i18n.t("hotkey.need_mod");
            return;
        }
        if (!hotkey_keys.try_map(e.Key, out var vk))
        {
            hint.Text = i18n.t("hotkey.unsupported");
            return;
        }

        captured = new hotkey_binding { mods = mods, key = vk };
        preview.Text = captured.display_name(i18n.is_english);
        hint.Text = string.Empty;
        ok_button.IsEnabled = true;
    }

    /// <summary>Opens the capture dialog; returns the chosen binding, or null on cancel.</summary>
    public static Task<hotkey_binding?> capture(Window owner, hotkey_binding? current)
        => new hotkey_capture(current).ShowDialog<hotkey_binding?>(owner);
}
