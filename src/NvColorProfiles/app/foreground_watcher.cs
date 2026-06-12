using System.Runtime.InteropServices;
using Avalonia.Threading;
using nv_color_profiles.interop;

namespace nv_color_profiles.app;

/// <summary>
/// Watches foreground-window changes via SetWinEventHook and raises <see cref="changed"/> with the
/// new process/title after a short debounce (so fast Alt-Tabbing doesn't thrash the hardware).
/// Must be created and used on the UI thread (the hook callback is delivered there).
/// </summary>
internal sealed class foreground_watcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int DEBOUNCE_MS = 150;

    private readonly win_event_proc callback; // field keeps the delegate alive for the hook
    private readonly DispatcherTimer debounce;
    private IntPtr hook;

    public event Action<string, string>? changed;

    public foreground_watcher()
    {
        callback = on_win_event;
        debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DEBOUNCE_MS) };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            var (process, title) = win_foreground.current();
            changed?.Invoke(process, title);
        };
    }

    public bool running => hook != IntPtr.Zero;

    /// <summary>Sets the delay the foreground must settle before <see cref="changed"/> fires (floor 50ms).</summary>
    public void set_delay(int milliseconds) => debounce.Interval = TimeSpan.FromMilliseconds(Math.Max(50, milliseconds));

    public void start()
    {
        if (running)
        {
            return;
        }
        hook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, callback, 0, 0, WINEVENT_OUTOFCONTEXT);

        // evaluate the current foreground immediately
        debounce.Stop();
        debounce.Start();
    }

    public void stop()
    {
        if (!running)
        {
            return;
        }
        UnhookWinEvent(hook);
        hook = IntPtr.Zero;
        debounce.Stop();
    }

    private void on_win_event(IntPtr hook_handle, uint event_type, IntPtr hwnd, int object_id, int child_id, uint thread, uint time)
    {
        // restart the debounce window on every foreground change
        debounce.Stop();
        debounce.Start();
    }

    public void Dispose() => stop();

    private delegate void win_event_proc(
        IntPtr hook_handle, uint event_type, IntPtr hwnd, int object_id, int child_id, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint event_min, uint event_max, IntPtr module, win_event_proc callback, uint process_id, uint thread_id, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
