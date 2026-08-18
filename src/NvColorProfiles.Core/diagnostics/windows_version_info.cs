using System.Runtime.Versioning;
using Microsoft.Win32;

namespace nv_color_profiles.core.diagnostics;

/// <summary>
/// Reads the Windows feature-version details from the registry ("24H2", "25H2", build number,
/// UBR). <see cref="Environment.OSVersion"/> only reports the kernel version and calls Win11
/// "10.0.26200" without ever mentioning that this is 25H2 — the feature-version string is what
/// bug-report readers actually recognise.
/// </summary>
[SupportedOSPlatform("windows")]
public static class windows_version_info
{
    /// <summary>
    /// Machine-facing snapshot. <see cref="product_name"/> is quietly wrong on Windows 11 (still
    /// says "Windows 10 ..." due to a Microsoft-side registry gaffe), so consumers should combine
    /// <see cref="display_version"/> and <see cref="current_build"/> for a correct label —
    /// build >= 22000 is Windows 11.
    /// </summary>
    public sealed record snapshot(
        string product_name,
        string edition_id,
        string display_version,
        string current_build,
        int ubr);

    public static snapshot? query()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null)
            {
                return null;
            }
            // Intentionally NOT reading ProductId, InstallDate, or the digital-license fields —
            // those are per-installation identifiers.
            return new snapshot(
                product_name: (string?)key.GetValue("ProductName") ?? "",
                edition_id: (string?)key.GetValue("EditionID") ?? "",
                display_version: (string?)key.GetValue("DisplayVersion") ?? "",
                current_build: (string?)key.GetValue("CurrentBuild") ?? "",
                ubr: (int?)key.GetValue("UBR") ?? 0);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Formats the snapshot into a single human line (e.g. <c>Windows 11 25H2 (build 26200.5123)</c>).</summary>
    public static string format(snapshot? s)
    {
        if (s is null)
        {
            return "(unknown)";
        }
        var family = int.TryParse(s.current_build, out var build) && build >= 22000
            ? "Windows 11"
            : "Windows 10";
        var version = string.IsNullOrEmpty(s.display_version) ? "(no feature version)" : s.display_version;
        var edition = string.IsNullOrEmpty(s.edition_id) ? "" : $" {s.edition_id}";
        var build_str = string.IsNullOrEmpty(s.current_build) ? "?" : s.current_build;
        return $"{family}{edition} {version} (build {build_str}.{s.ubr})";
    }
}
