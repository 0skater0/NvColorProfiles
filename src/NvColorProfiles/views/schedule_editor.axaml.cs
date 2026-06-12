using Avalonia.Controls;
using nv_color_profiles.core.rules;
using nv_color_profiles.localization;

namespace nv_color_profiles.views;

public partial class schedule_editor : Window
{
    public schedule_editor()
    {
        InitializeComponent();
    }

    private schedule_editor(IReadOnlyList<string> profiles, schedule_entry? existing) : this()
    {
        Title = existing is null ? i18n.t("schedule.new") : i18n.t("schedule.edit");

        from_time.SelectedTime = parse(existing?.from ?? "20:00");
        to_time.SelectedTime = parse(existing?.to ?? "23:00");

        foreach (var p in profiles)
        {
            profile_combo.Items.Add(p);
        }
        if (existing is not null)
        {
            profile_combo.SelectedItem = existing.profile;
        }
        if (profile_combo.SelectedIndex < 0 && profile_combo.ItemCount > 0)
        {
            profile_combo.SelectedIndex = 0;
        }

        ok_button.Click += (_, _) =>
        {
            if (profile_combo.SelectedItem is not string profile)
            {
                Close(null);
                return;
            }
            Close(new schedule_entry
            {
                from = format(from_time.SelectedTime),
                to = format(to_time.SelectedTime),
                profile = profile,
            });
        };
        cancel_button.Click += (_, _) => Close(null);
    }

    public static Task<schedule_entry?> edit(Window owner, IReadOnlyList<string> profiles, schedule_entry? existing)
        => new schedule_editor(profiles, existing).ShowDialog<schedule_entry?>(owner);

    private static TimeSpan parse(string hhmm)
    {
        var parts = hhmm.Split(':');
        var hour = parts.Length > 0 && int.TryParse(parts[0], out var h) ? Math.Clamp(h, 0, 23) : 0;
        var minute = parts.Length > 1 && int.TryParse(parts[1], out var m) ? Math.Clamp(m, 0, 59) : 0;
        return new TimeSpan(hour, minute, 0);
    }

    private static string format(TimeSpan? time)
    {
        var t = time ?? TimeSpan.Zero;
        return $"{t.Hours:00}:{t.Minutes:00}";
    }
}
