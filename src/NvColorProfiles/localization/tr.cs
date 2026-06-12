using Avalonia.Markup.Xaml;

namespace nv_color_profiles.localization;

/// <summary>
/// XAML markup extension: <c>{i18n:tr key}</c> resolves to the localized string for the active
/// language at load time. Windows are re-created when reopened, so a language switch is picked up.
/// </summary>
public sealed class tr : MarkupExtension
{
    public tr()
    {
    }

    public tr(string key) => this.key = key;

    public string key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => i18n.t(key);
}
