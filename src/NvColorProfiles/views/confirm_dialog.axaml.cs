using Avalonia.Controls;

namespace nv_color_profiles.views;

public partial class confirm_dialog : Window
{
    public confirm_dialog()
    {
        InitializeComponent();
    }

    private confirm_dialog(string title, string text, string confirm_label, string cancel_label) : this()
    {
        Title = title;
        message.Text = text;
        confirm_button.Content = confirm_label;
        cancel_button.Content = cancel_label;
        confirm_button.Click += (_, _) => Close(true);
        cancel_button.Click += (_, _) => Close(false);
    }

    /// <summary>Shows a yes/no confirmation; returns true only if the confirm button was clicked.</summary>
    public static Task<bool> ask(Window owner, string title, string text, string confirm_label, string cancel_label)
        => new confirm_dialog(title, text, confirm_label, cancel_label).ShowDialog<bool>(owner);
}
