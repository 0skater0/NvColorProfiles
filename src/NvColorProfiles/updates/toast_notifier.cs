using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace nv_color_profiles.updates;

/// <summary>
/// Windows system-toast wrapper for the "update available" notification. Failure is silent: the
/// tray badge and the manual "Check now" button provide alternative paths, so the toast is a
/// best-effort surface only.
/// </summary>
internal static class toast_notifier
{
    private const string AUMID = "NvColorProfiles.App";
    private const string DISPLAY_NAME = "NvColorProfiles";
    private const string ARG_URL = "url";

    private static bool activation_hooked;

    /// <summary>Shows a "Update available" toast. Clicking it opens the release URL in the default
    /// browser through the toolkit's activation event.</summary>
    public static void show_update_available(string title, string body, string release_url, ILogger log)
    {
        try
        {
            set_current_process_aumid(log);
            ensure_activation_hooked(log);

            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .AddArgument(ARG_URL, release_url)
                .Show();
        }
        catch (Exception ex)
        {
            log.LogInformation(ex, "Toast notification failed; tray badge remains as the fallback");
        }
    }

    private static void ensure_activation_hooked(ILogger log)
    {
        if (activation_hooked)
        {
            return;
        }
        try
        {
            ToastNotificationManagerCompat.OnActivated += args =>
            {
                var parsed = ToastArguments.Parse(args.Argument);
                if (!parsed.TryGetValue(ARG_URL, out var target) || string.IsNullOrWhiteSpace(target))
                {
                    return;
                }
                try
                {
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    log.LogInformation(ex, "Opening release URL from toast failed");
                }
            };
            activation_hooked = true;
        }
        catch (Exception ex)
        {
            log.LogInformation(ex, "Could not hook toast activation; click will be a no-op");
        }
    }

    // Windows requires an AUMID for classic desktop toasts. Without one, .Show() silently produces
    // nothing on some SKUs, and without a matching Start-Menu shortcut Windows shows no app icon.
    // The value must be stable across process restarts.
    private static void set_current_process_aumid(ILogger log)
    {
        try
        {
            var exe_path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe_path))
            {
                aumid_shortcut.ensure(AUMID, DISPLAY_NAME, exe_path, log);
            }
        }
        catch
        {
            // best-effort; falls through to the AUMID assignment below
        }
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AUMID);
        }
        catch
        {
            // best-effort; the toolkit compat layer may still succeed
        }
    }

    /// <summary>Ensures the AUMID-tagged Start-Menu shortcut exists without firing a toast.
    /// Called from the installer post-install step (<c>--register-toast</c>).</summary>
    public static void ensure_shortcut(ILogger log)
    {
        var exe_path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe_path))
        {
            aumid_shortcut.ensure(AUMID, DISPLAY_NAME, exe_path, log);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
}
