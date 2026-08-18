using Avalonia.Controls;

namespace nv_color_profiles.views;

/// <summary>Modal shown once, before the first update check runs. The returned bool is the user's
/// choice for the opt-in checkbox; the caller persists it into <c>app_settings.update_check_enabled</c>
/// and flips <c>update_check_prompted</c> so the modal never reappears.</summary>
public partial class first_run_dialog : Window
{
    private readonly TaskCompletionSource<bool> completion = new();

    public first_run_dialog()
    {
        InitializeComponent();
        continue_button.Click += (_, _) => Close(opt_in_check.IsChecked ?? true);
        // the app has no main window, so Close returns to nothing — capture the result on Closed
        Closed += (_, _) => completion.TrySetResult(opt_in_check.IsChecked ?? true);
    }

    /// <summary>Shows the dialog as a top-level window (this is a tray app with no parent) and
    /// resolves once the user has clicked Continue or closed the window.</summary>
    public static Task<bool> ask()
    {
        var dialog = new first_run_dialog();
        dialog.Show();
        return dialog.completion.Task;
    }
}
