using Avalonia.Controls;

namespace nv_color_profiles.views;

public partial class text_prompt : Window
{
    public text_prompt()
    {
        InitializeComponent();
    }

    private text_prompt(string title, string initial) : this()
    {
        Title = title;
        input.Text = initial;
        ok_button.Click += (_, _) => Close(string.IsNullOrWhiteSpace(input.Text) ? null : input.Text!.Trim());
        cancel_button.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            input.SelectAll();
            input.Focus();
        };
    }

    public static Task<string?> ask(Window owner, string title, string initial)
        => new text_prompt(title, initial).ShowDialog<string?>(owner);
}
