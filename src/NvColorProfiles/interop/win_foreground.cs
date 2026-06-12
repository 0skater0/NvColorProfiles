using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace nv_color_profiles.interop;

/// <summary>Reads the current foreground window's process name and title.</summary>
internal static class win_foreground
{
    /// <summary>Returns (process_name incl. ".exe", window_title); empty strings when unavailable.</summary>
    public static (string process, string title) current()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return (string.Empty, string.Empty);
        }
        return (process_name(hwnd), window_title(hwnd));
    }

    private static string window_title(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }
        var buffer = new StringBuilder(length + 1);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string process_name(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
            {
                return string.Empty;
            }
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe"; // ProcessName carries no extension
        }
        catch
        {
            return string.Empty; // process gone / access denied
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max_count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process_id);
}
