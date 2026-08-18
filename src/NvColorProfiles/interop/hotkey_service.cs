using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using nv_color_profiles.core.profiles;

namespace nv_color_profiles.interop;

/// <summary>
/// System-wide hotkeys. Registering with a NULL window posts WM_HOTKEY to the registering thread's
/// queue, so a dedicated thread with a plain GetMessage loop receives them — no hidden window or
/// WndProc needed. The same thread also hosts the low-level mouse hook for XButton bindings, so
/// both live on one thread-affine message loop.
/// </summary>
internal sealed class hotkey_service : IDisposable
{
    /// <summary>What a triggered binding asks the app to do. The <c>payload</c> disambiguates
    /// per-instance actions (e.g. which profile to apply for <see cref="apply_profile"/>).</summary>
    public enum hotkey_kind
    {
        profile_next,
        profile_prev,
        toggle_auto,
        apply_profile,
    }

    /// <summary>Raised on the hotkey thread — marshal to the UI thread before touching app state.</summary>
    public event Action<hotkey_kind, string?>? triggered;

    /// <summary>One registered hotkey. <see cref="mouse_button"/> non-zero routes via the mouse hook
    /// instead of RegisterHotKey. <see cref="id"/> is the wparam that WM_HOTKEY carries back.</summary>
    public sealed record binding(int id, hotkey_kind kind, string? payload, uint mods, uint vk, uint mouse_button = 0);

    private const uint MOD_NOREPEAT = 0x4000;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const uint PM_NOREMOVE = 0x0000;

    private readonly ILogger<hotkey_service> log;
    private readonly ILoggerFactory loggers;
    private mouse_hook? mouse;
    private Thread? thread;
    private uint thread_id;
    private IReadOnlyList<binding> bindings = Array.Empty<binding>();

    public hotkey_service(ILogger<hotkey_service> log, ILoggerFactory loggers)
    {
        this.log = log;
        this.loggers = loggers;
    }

    /// <summary>Sets the hotkeys to register. Call before <see cref="start"/> (stop/set/start to rebind).</summary>
    public void set_bindings(IReadOnlyList<binding> value) => bindings = value;

    public void start()
    {
        if (thread is not null)
        {
            return;
        }
        using var ready = new ManualResetEventSlim(false);
        thread = new Thread(() => run(ready)) { IsBackground = true, Name = "nvcp-hotkeys" };
        thread.Start();
        if (!ready.Wait(2000))
        {
            log.LogWarning("Hotkey thread did not become ready in time");
        }
    }

    public void stop()
    {
        if (thread is null)
        {
            return;
        }
        if (thread_id != 0)
        {
            PostThreadMessage(thread_id, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        thread.Join(2000);
        thread = null;
        thread_id = 0;
    }

    private void run(ManualResetEventSlim ready)
    {
        thread_id = GetCurrentThreadId();
        // RegisterHotKey with a NULL window needs the thread to own a message queue first
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
        register_all();
        ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY)
            {
                dispatch_by_id(msg.wParam.ToInt32());
            }
        }

        unregister_all();
    }

    private void dispatch_by_id(int id)
    {
        var match = find_binding(id);
        if (match is not null)
        {
            triggered?.Invoke(match.kind, match.payload);
        }
    }

    private binding? find_binding(int id)
    {
        for (var i = 0; i < bindings.Count; i++)
        {
            if (bindings[i].id == id)
            {
                return bindings[i];
            }
        }
        return null;
    }

    private void register_all()
    {
        var mouse_bindings = new List<mouse_hook.mouse_binding>();
        foreach (var b in bindings)
        {
            if (b.mouse_button != 0)
            {
                mouse_bindings.Add(new mouse_hook.mouse_binding(b.id, b.mods, b.mouse_button));
                continue;
            }
            if (b.vk == 0)
            {
                continue; // unset binding
            }
            // MOD_NOREPEAT is a registration concern, not stored in the binding
            if (!RegisterHotKey(IntPtr.Zero, b.id, b.mods | MOD_NOREPEAT, b.vk))
            {
                log.LogWarning(
                    "Hotkey {combo} could not be registered (likely claimed by another app)",
                    hotkey_binding.describe(b.mods, b.vk, english: true));
            }
        }
        if (mouse_bindings.Count > 0)
        {
            mouse ??= new mouse_hook(loggers.CreateLogger<mouse_hook>());
            mouse.install(mouse_bindings, dispatch_by_id);
        }
    }

    private void unregister_all()
    {
        foreach (var b in bindings)
        {
            if (b.mouse_button == 0 && b.vk != 0)
            {
                UnregisterHotKey(IntPtr.Zero, b.id);
            }
        }
        mouse?.uninstall();
    }

    public void Dispose()
    {
        stop();
        mouse?.Dispose();
        mouse = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
