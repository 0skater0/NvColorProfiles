using Microsoft.Win32;

namespace nv_color_profiles.app;

/// <summary>
/// Windows autostart via the per-user Run key. Platform-specific, so it lives in the Windows app
/// layer rather than the headless core. The registry is the single source of truth; all operations
/// are best-effort and never throw into the UI.
/// </summary>
internal static class autostart
{
    private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string VALUE_NAME = "NvColorProfiles";

    public static bool is_enabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY);
            return key?.GetValue(VALUE_NAME) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void set(bool enabled, string executable_path)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RUN_KEY);

            if (enabled)
            {
                key.SetValue(VALUE_NAME, $"\"{executable_path}\"");
            }
            else
            {
                key.DeleteValue(VALUE_NAME, throwOnMissingValue: false);
            }
        }
        catch
        {
            // best-effort — the UI re-reads is_enabled() to reflect the actual state
        }
    }
}
