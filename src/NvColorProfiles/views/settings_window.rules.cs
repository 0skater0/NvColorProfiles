using System.Globalization;
using Avalonia.Input;
using nv_color_profiles.core.rules;
using nv_color_profiles.localization;

namespace nv_color_profiles.views;

// Rules tab: the auto-switch rule list and the switch-delay control.
public partial class settings_window
{
    private void refresh_rules()
    {
        rules_list.Items.Clear();
        for (var i = 0; i < working.rules.Count; i++)
        {
            var r = working.rules[i];
            var kind = r.type == match_type.process ? i18n.t("kind.process") : i18n.t("kind.title");
            rules_list.Items.Add($"{i + 1}.  [{kind}]  {r.value}  →  {r.profile}");
        }
    }

    private async Task on_rule_new()
    {
        var r = await rule_editor.edit(this, profile_names(), null);
        if (r is not null)
        {
            working.rules.Add(r);
            refresh_rules();
        }
    }

    private async Task on_rule_edit()
    {
        var i = rules_list.SelectedIndex;
        if (i < 0 || i >= working.rules.Count)
        {
            return;
        }
        var r = await rule_editor.edit(this, profile_names(), working.rules[i]);
        if (r is not null)
        {
            working.rules[i] = r;
            refresh_rules();
        }
    }

    private void on_rule_delete()
    {
        var i = rules_list.SelectedIndex;
        if (i < 0 || i >= working.rules.Count)
        {
            return;
        }
        working.rules.RemoveAt(i);
        refresh_rules();
    }

    private void move_rule(int direction)
    {
        var i = rules_list.SelectedIndex;
        var j = i + direction;
        if (i < 0 || j < 0 || j >= working.rules.Count)
        {
            return;
        }
        (working.rules[i], working.rules[j]) = (working.rules[j], working.rules[i]);
        refresh_rules();
        rules_list.SelectedIndex = j;
    }

    private async void on_delay_direct_input(object? sender, PointerPressedEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            await prompt_delay();
        }
    }

    private async void on_delay_slider_pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }
        e.Handled = true; // don't let the slider jump to the click position
        await prompt_delay();
    }

    private async Task prompt_delay()
    {
        var current = (delay_slider.Value / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
        var input = await text_prompt.ask(this, i18n.t("delay_prompt"), current);
        if (input is null)
        {
            return;
        }
        if (double.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            delay_slider.Value = Math.Clamp(seconds * 1000.0, delay_slider.Minimum, delay_slider.Maximum);
        }
    }
}
