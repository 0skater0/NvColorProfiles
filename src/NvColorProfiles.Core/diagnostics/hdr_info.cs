using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace nv_color_profiles.core.diagnostics;

/// <summary>Named modes for the v2 <c>activeColorMode</c> field, plus a sentinel for v1 fallback.</summary>
public enum hdr_active_mode
{
    unknown = -1,
    sdr = 0,
    wide_color = 1,
    hdr = 2,
}

/// <summary>
/// Snapshot of the driver's advanced-color state for one display. Kept as a plain record without
/// any platform attribute so callers on any target can pass it around; only the code that
/// actually queries the Windows API in <see cref="hdr_info"/> is Windows-gated.
/// </summary>
public sealed record hdr_snapshot(
    bool supported,
    bool enabled,
    bool wide_color_supported,
    bool wide_color_user_enabled,
    bool hdr_supported,
    bool hdr_user_enabled,
    hdr_active_mode active,
    uint color_encoding,
    uint bits_per_channel,
    bool from_v2);

/// <summary>
/// Reads the HDR / Advanced Color state per display. HDR reshapes the entire color pipeline, so
/// this is critical context when a user reports colors looking "off" — our gamma ramp lands in a
/// different pipeline stage when HDR is on than when it is off.
///
/// Tries the Win11 24H2+ v2 struct first (Type 15, separates SDR/WCG/HDR cleanly) and falls back
/// to the v1 struct (Type 9, since Win10 1709, only "supported/enabled" flags). Both are queried
/// through <see cref="display_info.enumerate"/> using the adapter/target identifiers.
/// </summary>
[SupportedOSPlatform("windows")]
public static class hdr_info
{
    private const uint GET_ADVANCED_COLOR_INFO = 9;
    private const uint GET_ADVANCED_COLOR_INFO_2 = 15;

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public display_info.DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;               // Bit0 = supported, Bit1 = enabled, Bit2 = wideColorEnforced, Bit3 = forceDisabled
        public uint colorEncoding;       // 0=RGB, 1=YCBCR444, 2=YCBCR422, 3=YCBCR420, 4=Intensity
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
    {
        public display_info.DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;               // See snapshot below.
        public uint colorEncoding;
        public uint bitsPerColorChannel;
        public uint activeColorMode;     // 0=SDR, 1=WCG, 2=HDR
    }

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO pkt);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 pkt);

    /// <summary>
    /// Reads the current HDR state for a given adapter/target. Returns <c>null</c> if both v2 and
    /// v1 queries fail (rare, would indicate a driver bug or a display that vanished mid-query).
    /// </summary>
    public static hdr_snapshot? query(display_info.LUID adapter_id, uint target_id)
    {
        var v2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
        {
            header = new display_info.DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = GET_ADVANCED_COLOR_INFO_2,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>(),
                adapterId = adapter_id,
                id = target_id,
            },
        };
        if (DisplayConfigGetDeviceInfo(ref v2) == 0)
        {
            return new hdr_snapshot(
                supported: (v2.value & 0x01) != 0,
                enabled: (v2.value & 0x02) != 0,
                wide_color_supported: (v2.value & 0x20) != 0,
                wide_color_user_enabled: (v2.value & 0x40) != 0,
                hdr_supported: (v2.value & 0x08) != 0,
                hdr_user_enabled: (v2.value & 0x10) != 0,
                active: v2.activeColorMode switch
                {
                    0 => hdr_active_mode.sdr,
                    1 => hdr_active_mode.wide_color,
                    2 => hdr_active_mode.hdr,
                    _ => hdr_active_mode.unknown,
                },
                color_encoding: v2.colorEncoding,
                bits_per_channel: v2.bitsPerColorChannel,
                from_v2: true);
        }

        var v1 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            header = new display_info.DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = GET_ADVANCED_COLOR_INFO,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                adapterId = adapter_id,
                id = target_id,
            },
        };
        if (DisplayConfigGetDeviceInfo(ref v1) != 0)
        {
            return null;
        }
        return new hdr_snapshot(
            supported: (v1.value & 0x01) != 0,
            enabled: (v1.value & 0x02) != 0,
            wide_color_supported: false,
            wide_color_user_enabled: false,
            hdr_supported: false,
            hdr_user_enabled: false,
            active: hdr_active_mode.unknown,
            color_encoding: v1.colorEncoding,
            bits_per_channel: v1.bitsPerColorChannel,
            from_v2: false);
    }
}
