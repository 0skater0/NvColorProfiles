using System.Runtime.Versioning;
using Microsoft.Win32;

namespace nv_color_profiles.core.diagnostics;

/// <summary>
/// Parses the NVIDIA Control Panel's persisted color state out of
/// <c>HKCU\Software\NVIDIA Corporation\Global\NVTweak\Devices\*\Color</c> into a human-readable
/// snapshot per device. NVCP writes the same color-correction pipeline stage we do, so a bug
/// report where "my sliders don't seem to do anything" often boils down to NVCP still owning the
/// pipeline and reasserting its own values on top of ours.
///
/// The raw registry dump is still shipped separately (see <c>diagnostic_bundle</c>) — this
/// helper is the aggregated, labelled version that a human can scan in seconds.
/// </summary>
[SupportedOSPlatform("windows")]
public static class nvcp_state
{
    private const string DEVICES_KEY = @"Software\NVIDIA Corporation\Global\NVTweak\Devices";

    // NVCP stores brightness/contrast/gamma per RGB channel as nine DWORDs indexed by opaque
    // magic numbers. The values below are the ones that appear in every observed dump — driver
    // versions from 2020 through 2026 all use these keys.
    private const string BRIGHTNESS_R = "3473410";
    private const string BRIGHTNESS_G = "3473411";
    private const string BRIGHTNESS_B = "3473412";
    private const string CONTRAST_R = "3473413";
    private const string CONTRAST_G = "3473414";
    private const string CONTRAST_B = "3473415";
    private const string GAMMA_R = "3473416";
    private const string GAMMA_G = "3473417";
    private const string GAMMA_B = "3473418";

    private const string VIBRANCE_R = "3538946";
    private const string VIBRANCE_G = "3538947";
    private const string VIBRANCE_B = "3538948";
    private const string HUE_R = "3538949";
    private const string HUE_G = "3538950";
    private const string HUE_B = "3538951";

    /// <summary>
    /// Per-device NVCP color state. <see cref="nvcp_device_id"/> is the numeric registry subkey
    /// name — it is not the NvAPI display id and often does not correspond one-to-one to a
    /// currently connected monitor (NVCP keeps entries for every panel it has ever seen).
    /// </summary>
    public sealed record device(
        string nvcp_device_id,
        bool color_correction_enabled,
        bool has_external_colors,
        bool external_colors_is_identity,
        string? icc_profile_filename,
        int brightness_r,
        int brightness_g,
        int brightness_b,
        int contrast_r,
        int contrast_g,
        int contrast_b,
        int gamma_r,
        int gamma_g,
        int gamma_b,
        int vibrance_r,
        int vibrance_g,
        int vibrance_b,
        int hue_r,
        int hue_g,
        int hue_b)
    {
        /// <summary>True when every slider sits at the driver's neutral value (100).</summary>
        public bool at_default =>
            brightness_r == 100 && brightness_g == 100 && brightness_b == 100 &&
            contrast_r == 100 && contrast_g == 100 && contrast_b == 100 &&
            gamma_r == 100 && gamma_g == 100 && gamma_b == 100 &&
            vibrance_r == 100 && vibrance_g == 100 && vibrance_b == 100 &&
            hue_r == 100 && hue_g == 100 && hue_b == 100;
    }

    /// <summary>Enumerates the NVTweak\Devices subtree. Returns an empty list when the key is absent.</summary>
    public static IReadOnlyList<device> enumerate()
    {
        var result = new List<device>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(DEVICES_KEY);
            if (root is null)
            {
                return result;
            }
            foreach (var sub_name in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(sub_name);
                using var color = sub?.OpenSubKey("Color");
                if (color is null)
                {
                    continue;
                }
                if (color.GetValueNames().Length == 0)
                {
                    continue;   // NVCP creates an empty Color key for every panel it has seen.
                }

                var icc_raw = color.GetValue("NvCplICCProfile") as string;
                result.Add(new device(
                    nvcp_device_id: sub_name,
                    color_correction_enabled: read_dword(color, "NvCplUseColorCorrection") == 1,
                    has_external_colors: color.GetValue("NvCplExternalColors") is byte[],
                    external_colors_is_identity: is_identity_ramp(color.GetValue("NvCplExternalColors") as byte[]),
                    icc_profile_filename: strip_nvcp_cache_suffix(icc_raw is null ? null : Path.GetFileName(icc_raw)),
                    brightness_r: read_dword(color, BRIGHTNESS_R, fallback: 100),
                    brightness_g: read_dword(color, BRIGHTNESS_G, fallback: 100),
                    brightness_b: read_dword(color, BRIGHTNESS_B, fallback: 100),
                    contrast_r: read_dword(color, CONTRAST_R, fallback: 100),
                    contrast_g: read_dword(color, CONTRAST_G, fallback: 100),
                    contrast_b: read_dword(color, CONTRAST_B, fallback: 100),
                    gamma_r: read_dword(color, GAMMA_R, fallback: 100),
                    gamma_g: read_dword(color, GAMMA_G, fallback: 100),
                    gamma_b: read_dword(color, GAMMA_B, fallback: 100),
                    vibrance_r: read_dword(color, VIBRANCE_R, fallback: 100),
                    vibrance_g: read_dword(color, VIBRANCE_G, fallback: 100),
                    vibrance_b: read_dword(color, VIBRANCE_B, fallback: 100),
                    hue_r: read_dword(color, HUE_R, fallback: 100),
                    hue_g: read_dword(color, HUE_G, fallback: 100),
                    hue_b: read_dword(color, HUE_B, fallback: 100)));
            }
        }
        catch
        {
            // Deliberately swallowed — the raw registry dump is still shipped in the bundle.
        }
        return result;
    }

    private static int read_dword(RegistryKey key, string name, int fallback = 0)
    {
        return key.GetValue(name) switch
        {
            int i => i,
            long l => (int)l,
            _ => fallback,
        };
    }

    // NVCP writes NvCplICCProfile with a numeric cache marker appended to the filename, so a
    // freshly assigned sRGB profile appears as "sRGB Color Space Profile.icm412024722". The
    // trailing digits are NVCP-internal and not part of the actual file on disk — strip them
    // so a bug reader sees the real filename.
    private static string? strip_nvcp_cache_suffix(string? filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return filename;
        }
        var match = System.Text.RegularExpressions.Regex.Match(
            filename, @"^(.+?\.(?:icm|icc))\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : filename;
    }

    // NvCplExternalColors is a 1536-byte LUT (256 entries × 3 channels × 2 bytes). Different
    // driver versions store it in one of two encodings that are both visually identity:
    // ushort little-endian per entry (`00 00 01 00 02 00 ...`) or byte-doubled per entry
    // (`00 00 01 01 02 02 ...`). In both, the low byte of the entry tracks the entry index for
    // an identity ramp; checking the low byte alone accepts either encoding while still
    // rejecting genuine custom LUTs where an entry drifts off the diagonal. Checking one
    // channel is enough — NVCP writes all three in lockstep. Requires the full 1536-byte
    // payload so a truncated blob does not claim identity by accident.
    private static bool is_identity_ramp(byte[]? blob)
    {
        if (blob is null || blob.Length < 1536)
        {
            return false;
        }
        for (var i = 0; i < 256; i++)
        {
            if (blob[i * 2] != (byte)i)
            {
                return false;
            }
        }
        return true;
    }
}
