using System.Runtime.InteropServices;

namespace nv_color_profiles.core.interop;

/// <summary>Thin user32 helpers for resolving monitor metadata that NvAPI does not expose directly.</summary>
internal static class win_display
{
    /// <summary>
    /// Best-effort generic monitor name for a GDI adapter device — fallback only; the real model
    /// name comes from <see cref="win_monitor_names"/> (DisplayConfig). Returns null when unavailable.
    /// </summary>
    public static string? try_get_monitor_name(string gdi_device_name)
    {
        try
        {
            var info = new display_device { cb = Marshal.SizeOf<display_device>() };
            // dev_num 0 = the monitor attached to the given adapter device
            if (enum_display_devices(gdi_device_name, 0, ref info, 0) && !string.IsNullOrWhiteSpace(info.device_string))
            {
                return info.device_string;
            }
        }
        catch
        {
            // best-effort only — a missing friendly name is not an error
        }
        return null;
    }

    // DllImport (not LibraryImport): the struct uses ByValTStr fields, which the source-generated
    // marshaller does not support without custom marshallers — DllImport is the pragmatic choice here.
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool enum_display_devices(string? device, uint dev_num, ref display_device info, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct display_device
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string device_name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string device_string;
        public uint state_flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string device_id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string device_key;
    }
}
