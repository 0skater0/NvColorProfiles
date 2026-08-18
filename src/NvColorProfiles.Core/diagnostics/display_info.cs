using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace nv_color_profiles.core.diagnostics;

/// <summary>
/// Enumerates active display targets via the Windows DisplayConfig API and reports the
/// EDID-derived monitor model name alongside the GDI device name. This is the only reliable
/// way to give bug-report readers a human-recognisable label ("LG UltraGear 34GN850") for a
/// display instead of the opaque NvAPI display id.
///
/// PII: the EDID vendor/product ids and the friendly name are safe (they describe the panel,
/// not the user). The <c>monitorDevicePath</c> field carries an instance GUID and is intentionally
/// not exposed.
/// </summary>
[SupportedOSPlatform("windows")]
public static class display_info
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    private const uint GET_SOURCE_NAME = 1;
    private const uint GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    // The MODE_INFO struct is an 80-byte union on x64 (16-byte header + 64-byte target-mode /
    // source-mode / desktop-image-info union). We never inspect its contents, so declare only
    // the size to keep the array layout correct for QueryDisplayConfig. Under-declaring the
    // element size makes QueryDisplayConfig return ERROR_INSUFFICIENT_BUFFER or, worse, write
    // past the managed array on real hardware.
    [StructLayout(LayoutKind.Sequential, Size = 80)]
    private struct DISPLAYCONFIG_MODE_INFO { }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags, ref uint pathCount, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint modeCount, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME pkt);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME pkt);

    /// <summary>
    /// One active display target with the GDI device name (<c>\\.\DISPLAY1</c>), the EDID
    /// friendly name, and the adapter/target identifiers needed for downstream HDR queries.
    /// </summary>
    public sealed record entry(
        string gdi_name,
        string friendly_name,
        bool friendly_name_from_edid,
        string edid_vendor,
        ushort edid_product_code,
        LUID adapter_id,
        uint target_id);

    /// <summary>
    /// Enumerates every active display path. Returns an empty list on non-Windows platforms or
    /// when the DisplayConfig API fails, never throws — this feeds a diagnostic bundle and must
    /// degrade gracefully.
    /// </summary>
    public static IReadOnlyList<entry> enumerate()
    {
        var result = new List<entry>();
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pc, out var mc) != 0)
        {
            return result;
        }
        var paths = new DISPLAYCONFIG_PATH_INFO[pc];
        var modes = new DISPLAYCONFIG_MODE_INFO[mc];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pc, paths, ref mc, modes, IntPtr.Zero) != 0)
        {
            return result;
        }

        for (var i = 0; i < pc; i++)
        {
            var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = GET_SOURCE_NAME,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                    adapterId = paths[i].sourceInfo.adapterId,
                    id = paths[i].sourceInfo.id,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref src) != 0)
            {
                continue;
            }

            var tgt = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = GET_TARGET_NAME,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                    adapterId = paths[i].targetInfo.adapterId,
                    id = paths[i].targetInfo.id,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref tgt) != 0)
            {
                continue;
            }

            result.Add(new entry(
                gdi_name: src.viewGdiDeviceName ?? "",
                friendly_name: tgt.monitorFriendlyDeviceName ?? "",
                friendly_name_from_edid: (tgt.flags & 0x1) != 0,
                edid_vendor: decode_edid_vendor(tgt.edidManufactureId),
                edid_product_code: tgt.edidProductCodeId,
                adapter_id: paths[i].targetInfo.adapterId,
                target_id: paths[i].targetInfo.id));
        }
        return result;
    }

    // EDID packs a three-letter manufacturer code into 15 bits (5 bits per letter, offset by 'A' - 1).
    private static string decode_edid_vendor(ushort raw)
    {
        if (raw == 0)
        {
            return "";
        }
        // The two bytes are big-endian on the wire; the Win32 API delivers them in native order,
        // and community reference implementations swap before decoding.
        var swapped = (ushort)((raw >> 8) | ((raw & 0xFF) << 8));
        var c1 = (char)(((swapped >> 10) & 0x1F) + 'A' - 1);
        var c2 = (char)(((swapped >> 5) & 0x1F) + 'A' - 1);
        var c3 = (char)((swapped & 0x1F) + 'A' - 1);
        // Reject decoded characters that fall outside A..Z (happens on garbled EDID with zero
        // nibbles — would otherwise produce '@' or control characters in the vendor field).
        if (c1 is < 'A' or > 'Z' || c2 is < 'A' or > 'Z' || c3 is < 'A' or > 'Z')
        {
            return "";
        }
        return $"{c1}{c2}{c3}";
    }
}
