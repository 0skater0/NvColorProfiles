using System.Runtime.Versioning;
using Microsoft.Win32;

namespace nv_color_profiles.core.diagnostics;

/// <summary>
/// Reads Windows' accessibility color filter state (grayscale, inverted, deuteranopia, etc.).
/// A user with an accidentally enabled color filter will see every screen through it, including
/// whatever gamma ramp we apply — this shows up in bug reports as "everything looks washed out"
/// or "everything is inverted".
/// </summary>
[SupportedOSPlatform("windows")]
public static class color_filter_info
{
    public enum filter_type
    {
        grayscale = 0,
        inverted = 1,
        grayscale_inverted = 2,
        deuteranopia = 3,
        protanopia = 4,
        tritanopia = 5,
    }

    public sealed record snapshot(bool active, filter_type type);

    public static snapshot query()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\ColorFiltering");
            if (key is null)
            {
                return new snapshot(active: false, type: filter_type.grayscale);
            }
            var active = (int?)key.GetValue("Active") == 1;
            var raw = (int?)key.GetValue("FilterType") ?? 0;
            var typed = Enum.IsDefined(typeof(filter_type), raw) ? (filter_type)raw : filter_type.grayscale;
            return new snapshot(active, typed);
        }
        catch
        {
            return new snapshot(active: false, type: filter_type.grayscale);
        }
    }
}
