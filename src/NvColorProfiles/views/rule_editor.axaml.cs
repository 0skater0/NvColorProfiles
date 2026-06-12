using System.Diagnostics;
using Avalonia.Controls;
using nv_color_profiles.core.rules;
using nv_color_profiles.localization;

namespace nv_color_profiles.views;

public partial class rule_editor : Window
{
    // one running window: exe name to match a process rule, title to match a window-title rule
    private sealed record app_entry(string exe, string title)
    {
        public override string ToString() => $"{exe} — {title}";
    }

    public rule_editor()
    {
        InitializeComponent();
    }

    private rule_editor(IReadOnlyList<string> profiles, rule? existing) : this()
    {
        Title = existing is null ? i18n.t("rule.new") : i18n.t("rule.edit");
        type_combo.Items.Add(i18n.t("match.process"));
        type_combo.Items.Add(i18n.t("match.title"));
        type_combo.SelectedIndex = existing?.type == match_type.window_title ? 1 : 0;
        value_box.Text = existing?.value ?? string.Empty;

        foreach (var entry in running_windowed_apps())
        {
            apps_combo.Items.Add(entry);
        }
        // picking an app fills the value with the exe name (process rule) or window title (title rule)
        apps_combo.SelectionChanged += (_, _) =>
        {
            if (apps_combo.SelectedItem is app_entry entry)
            {
                value_box.Text = type_combo.SelectedIndex == 1 ? entry.title : entry.exe;
            }
        };

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
            if (string.IsNullOrWhiteSpace(value_box.Text) || profile_combo.SelectedItem is not string profile)
            {
                Close(null);
                return;
            }
            var type = type_combo.SelectedIndex == 1 ? match_type.window_title : match_type.process;
            Close(new rule { type = type, value = value_box.Text!.Trim(), profile = profile });
        };
        cancel_button.Click += (_, _) => Close(null);
    }

    public static Task<rule?> edit(Window owner, IReadOnlyList<string> profiles, rule? existing)
        => new rule_editor(profiles, existing).ShowDialog<rule?>(owner);

    // visible top-level windows, sorted by exe name; per-process access can fail, so guard each
    private static IReadOnlyList<app_entry> running_windowed_apps()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<app_entry>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }
                var title = process.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }
                var exe = process.ProcessName + ".exe";
                if (seen.Add(exe + " " + title))
                {
                    apps.Add(new app_entry(exe, title));
                }
            }
            catch
            {
                // process exited or access denied — skip it
            }
            finally
            {
                process.Dispose();
            }
        }
        apps.Sort((a, b) => string.Compare(a.exe, b.exe, StringComparison.OrdinalIgnoreCase));
        return apps;
    }
}
