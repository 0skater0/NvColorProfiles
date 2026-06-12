using System.Runtime.InteropServices;

namespace nv_color_profiles.core.interop;

/// <summary>
/// GDI gamma-ramp application per display. Brightness/contrast/gamma are applied through the
/// Windows gamma ramp (the same mechanism the NVIDIA panel uses for these three sliders) on a
/// device context created for the specific monitor.
/// </summary>
internal static class win_gamma
{
    private const int RGB_BUFFER_LENGTH = 256 * 3;

    /// <summary>Applies an R|G|B (768-entry) ramp to the given GDI device (e.g. "\\.\DISPLAY1").</summary>
    public static bool apply(string gdi_device_name, ushort[] rgb_buffer)
    {
        if (rgb_buffer.Length != RGB_BUFFER_LENGTH)
        {
            throw new ArgumentException($"ramp buffer must be {RGB_BUFFER_LENGTH} entries", nameof(rgb_buffer));
        }

        var hdc = create_dc(gdi_device_name, gdi_device_name, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return set_device_gamma_ramp(hdc, rgb_buffer);
        }
        finally
        {
            delete_dc(hdc);
        }
    }

    /// <summary>Reads the current R|G|B (768-entry) ramp for the given GDI device, or null on failure.</summary>
    public static ushort[]? try_get(string gdi_device_name)
    {
        var hdc = create_dc(gdi_device_name, gdi_device_name, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new ushort[RGB_BUFFER_LENGTH];
            return get_device_gamma_ramp(hdc, buffer) ? buffer : null;
        }
        finally
        {
            delete_dc(hdc);
        }
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateDCW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr create_dc(string? driver, string? device, string? port, IntPtr dev_mode);

    [DllImport("gdi32.dll", EntryPoint = "DeleteDC")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool delete_dc(IntPtr hdc);

    [DllImport("gdi32.dll", EntryPoint = "SetDeviceGammaRamp")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool set_device_gamma_ramp(IntPtr hdc, ushort[] ramp);

    [DllImport("gdi32.dll", EntryPoint = "GetDeviceGammaRamp")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool get_device_gamma_ramp(IntPtr hdc, ushort[] ramp);
}
