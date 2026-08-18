using System.Runtime.Versioning;
using Microsoft.Win32;

namespace nv_color_profiles.core.diagnostics;

/// <summary>
/// Detects whether Windows' Night Light feature is currently active. Night Light overlays a warm
/// tint on top of every gamma ramp we set, so a user reporting "colors look yellow at night" is
/// often chasing Night Light, not us.
///
/// Windows persists the state as a Microsoft Bond CompactBinary blob under CloudStore. The format
/// is not documented; community tooling (nightlight-cli, adrilight) matches a two-byte signature
/// that appears in the blob when the toggle is on. Reliable enough for a diagnostic report —
/// never rely on it for driving app behaviour.
/// </summary>
[SupportedOSPlatform("windows")]
public static class night_light_info
{
    private const string STATE_KEY =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\" +
        @"default$windows.data.bluelightreduction.bluelightreductionstate\" +
        @"windows.data.bluelightreduction.bluelightreductionstate";

    /// <summary>Result of the probe. <see cref="present"/> is false if the user never opened the feature.</summary>
    public sealed record snapshot(bool present, bool enabled);

    /// <summary>
    /// Reads the current state. Returns <c>present=false</c> when the registry key does not exist
    /// (feature never touched) or the blob is too short to inspect.
    /// </summary>
    public static snapshot query()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(STATE_KEY);
            if (key?.GetValue("Data") is not byte[] blob || blob.Length < 24)
            {
                return new snapshot(present: false, enabled: false);
            }

            // Community convention: bytes 0x10 0x00 within the header window mark an active state.
            // Not a contract, but stable across the observable Windows 10/11 range.
            for (var i = 18; i < Math.Min(blob.Length - 1, 30); i++)
            {
                if (blob[i] == 0x10 && blob[i + 1] == 0x00)
                {
                    return new snapshot(present: true, enabled: true);
                }
            }
            return new snapshot(present: true, enabled: false);
        }
        catch
        {
            return new snapshot(present: false, enabled: false);
        }
    }
}
