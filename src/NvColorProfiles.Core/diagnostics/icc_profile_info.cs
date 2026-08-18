using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace nv_color_profiles.core.diagnostics;

/// <summary>
/// Reads the currently assigned ICC profile per display via the WCS API. An ICC profile bends
/// colors before they reach the panel and can make our gamma ramp look completely different from
/// what the user set in NvColorProfiles' sliders. We report only the profile filename, not the
/// full path — the path always sits under the user profile.
/// </summary>
[SupportedOSPlatform("windows")]
public static class icc_profile_info
{
    private enum WCS_PROFILE_MANAGEMENT_SCOPE
    {
        SystemWide = 0,
        CurrentUser = 1,
    }

    private enum COLORPROFILETYPE
    {
        ICC = 0,
        DMP = 1,
        CAMP = 2,
        GMMP = 3,
    }

    private enum COLORPROFILESUBTYPE
    {
        Perceptual = 0,
        RelativeColorimetric = 1,
        Saturation = 2,
        AbsoluteColorimetric = 3,
        None = 4,
        RgbWorkingSpace = 5,
        CustomWorkingSpace = 6,
        StandardDisplayColorMode = 7,
        ExtendedDisplayColorMode = 8,
    }

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WcsGetDefaultColorProfileSize(
        WCS_PROFILE_MANAGEMENT_SCOPE scope,
        [MarshalAs(UnmanagedType.LPWStr)] string? deviceName,
        COLORPROFILETYPE type,
        COLORPROFILESUBTYPE subtype,
        uint profileId,
        out uint cbProfileName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WcsGetDefaultColorProfile(
        WCS_PROFILE_MANAGEMENT_SCOPE scope,
        [MarshalAs(UnmanagedType.LPWStr)] string? deviceName,
        COLORPROFILETYPE type,
        COLORPROFILESUBTYPE subtype,
        uint profileId,
        uint cbProfileName,
        StringBuilder profileName);

    /// <summary>
    /// Hard cap for both WCS calls combined per display. On some systems mscms traverses a broken
    /// registry association chain or waits on a network profile store; a diagnostic export must
    /// not hang for that. Applied once around the whole lookup so a slow per-user probe cannot
    /// leave time-of-death on the system-wide fallback.
    /// </summary>
    private static readonly TimeSpan QUERY_TIMEOUT = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// Returns the profile filename (e.g. <c>sRGB Color Space Profile.icm</c>) or <c>null</c> if
    /// the query fails or exceeds <see cref="QUERY_TIMEOUT"/>. Both the per-user and system-wide
    /// scopes are consulted, per-user wins when present — that matches how the Windows Color
    /// Management dialog resolves the default.
    /// </summary>
    public static string? default_profile(string gdi_name)
    {
        try
        {
            var task = Task.Run(() =>
                read_scope(WCS_PROFILE_MANAGEMENT_SCOPE.CurrentUser, gdi_name)
                ?? read_scope(WCS_PROFILE_MANAGEMENT_SCOPE.SystemWide, gdi_name));
            return task.Wait(QUERY_TIMEOUT) ? task.Result : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? read_scope(WCS_PROFILE_MANAGEMENT_SCOPE scope, string gdi_name)
    {
        try
        {
            if (!WcsGetDefaultColorProfileSize(
                    scope, gdi_name, COLORPROFILETYPE.ICC, COLORPROFILESUBTYPE.None, 0, out var size)
                || size == 0)
            {
                return null;
            }
            var sb = new StringBuilder((int)size);
            if (!WcsGetDefaultColorProfile(
                    scope, gdi_name, COLORPROFILETYPE.ICC, COLORPROFILESUBTYPE.None, 0, size, sb))
            {
                return null;
            }
            var raw = sb.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : Path.GetFileName(raw);
        }
        catch
        {
            return null;
        }
    }
}
