using System.Runtime.InteropServices;

namespace nv_color_profiles.core.interop;

/// <summary>
/// Resolves real monitor model names (e.g. "LG ULTRAFINE") per GDI device ("\\.\DISPLAY1") via the
/// Windows DisplayConfig API — unlike EnumDisplayDevices, which only returns "Generic PnP Monitor".
/// </summary>
internal static class win_monitor_names
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    /// <summary>Map of GDI device name → friendly monitor name. Empty on any failure (best-effort).</summary>
    public static Dictionary<string, string> query()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var path_count, out var mode_count) != 0)
            {
                return map;
            }

            var paths = new displayconfig_path_info[path_count];
            var modes = new displayconfig_mode_info[mode_count];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref path_count, paths, ref mode_count, modes, IntPtr.Zero) != 0)
            {
                return map;
            }

            for (var i = 0; i < path_count; i++)
            {
                var path = paths[i];

                var source = new displayconfig_source_device_name();
                source.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                source.header.size = (uint)Marshal.SizeOf<displayconfig_source_device_name>();
                source.header.adapter_id = path.source_info.adapter_id;
                source.header.id = path.source_info.id;
                if (DisplayConfigGetDeviceInfo(ref source) != 0)
                {
                    continue;
                }

                var target = new displayconfig_target_device_name();
                target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = (uint)Marshal.SizeOf<displayconfig_target_device_name>();
                target.header.adapter_id = path.target_info.adapter_id;
                target.header.id = path.target_info.id;
                if (DisplayConfigGetDeviceInfo(ref target) != 0)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(source.view_gdi_device_name) && !string.IsNullOrWhiteSpace(target.monitor_friendly_device_name))
                {
                    map[source.view_gdi_device_name] = target.monitor_friendly_device_name;
                }
            }
        }
        catch
        {
            // best-effort — fall back to GDI names
        }
        return map;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct luid
    {
        public uint low_part;
        public int high_part;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct displayconfig_rational
    {
        public uint numerator;
        public uint denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct displayconfig_path_source_info
    {
        public luid adapter_id;
        public uint id;
        public uint mode_info_idx;
        public uint status_flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct displayconfig_path_target_info
    {
        public luid adapter_id;
        public uint id;
        public uint mode_info_idx;
        public uint output_technology;
        public uint rotation;
        public uint scaling;
        public displayconfig_rational refresh_rate;
        public uint scan_line_ordering;
        public int target_available;
        public uint status_flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct displayconfig_path_info
    {
        public displayconfig_path_source_info source_info;
        public displayconfig_path_target_info target_info;
        public uint flags;
    }

    // the mode-info union is not read here; reserve its 64-byte footprint so the array marshals
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct displayconfig_mode_info
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct displayconfig_device_info_header
    {
        public int type;
        public uint size;
        public luid adapter_id;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct displayconfig_source_device_name
    {
        public displayconfig_device_info_header header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string view_gdi_device_name;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct displayconfig_target_device_name
    {
        public displayconfig_device_info_header header;
        public uint flags;
        public uint output_technology;
        public ushort edid_manufacture_id;
        public ushort edid_product_code_id;
        public uint connector_instance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitor_friendly_device_name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitor_device_path;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint num_path_elements, out uint num_mode_elements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint num_path_elements, [Out] displayconfig_path_info[] path_array,
        ref uint num_mode_elements, [Out] displayconfig_mode_info[] mode_array,
        IntPtr current_topology_id);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref displayconfig_source_device_name request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref displayconfig_target_device_name request);
}
