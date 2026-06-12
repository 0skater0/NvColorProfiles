using System.Reflection;
using Avalonia.Controls;

namespace nv_color_profiles.views;

// Read-only viewer for the bundled license texts. The NOTICE + license files are embedded as
// resources (see NvColorProfiles.csproj) so the LGPL notice travels with the bare portable exe,
// not just as sibling files that get lost when the exe is copied elsewhere.
public partial class licenses_window : Window
{
    // embedded-resource logical name -> combo label (proper document names; not translated)
    private static readonly (string resource, string label)[] docs =
    {
        ("notice.txt", "Third-party notices (NOTICE)"),
        ("mit-license.txt", "NvColorProfiles (MIT License)"),
        ("lgpl-3.0.txt", "GNU LGPL-3.0"),
        ("gpl-3.0.txt", "GNU GPL-3.0"),
    };

    public licenses_window()
    {
        InitializeComponent();
        foreach (var (_, label) in docs)
        {
            doc_combo.Items.Add(label);
        }
        doc_combo.SelectionChanged += (_, _) => show_selected();
        close_button.Click += (_, _) => Close();
        doc_combo.SelectedIndex = 0;
    }

    private void show_selected()
    {
        var i = doc_combo.SelectedIndex;
        if (i >= 0 && i < docs.Length)
        {
            doc_text.Text = read_resource(docs[i].resource);
        }
    }

    private static string read_resource(string logical_name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logical_name);
        if (stream is null)
        {
            return $"({logical_name} not found)";
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Task show(Window owner) => new licenses_window().ShowDialog(owner);
}
