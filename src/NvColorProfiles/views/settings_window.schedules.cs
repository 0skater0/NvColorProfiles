namespace nv_color_profiles.views;

// Schedule tab: the time-of-day profile entries.
public partial class settings_window
{
    private void refresh_schedules()
    {
        schedule_list.Items.Clear();
        foreach (var s in working.schedules)
        {
            schedule_list.Items.Add($"{s.from} – {s.to}  →  {s.profile}");
        }
    }

    private async Task on_schedule_new()
    {
        var s = await schedule_editor.edit(this, profile_names(), null);
        if (s is not null)
        {
            working.schedules.Add(s);
            refresh_schedules();
            schedule_list.SelectedIndex = working.schedules.Count - 1;
        }
    }

    private async Task on_schedule_edit()
    {
        var i = schedule_list.SelectedIndex;
        if (i < 0 || i >= working.schedules.Count)
        {
            return;
        }
        var s = await schedule_editor.edit(this, profile_names(), working.schedules[i]);
        if (s is not null)
        {
            working.schedules[i] = s;
            refresh_schedules();
            schedule_list.SelectedIndex = i;
        }
    }

    private void on_schedule_delete()
    {
        var i = schedule_list.SelectedIndex;
        if (i < 0 || i >= working.schedules.Count)
        {
            return;
        }
        working.schedules.RemoveAt(i);
        refresh_schedules();
    }
}
