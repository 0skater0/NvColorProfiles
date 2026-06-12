using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using nv_color_profiles.core.profiles;

namespace nv_color_profiles.interop;

/// <summary>
/// System-wide hotkeys. Registering with a NULL window posts WM_HOTKEY to the registering thread's
/// queue, so a dedicated thread with a plain GetMessage loop receives them — no hidden window or
/// WndProc needed. Hotkeys are thread-affine: register/unregister both happen on that thread.
/// </summary>
internal sealed class hotkey_service : IDisposable
{
    public enum hotkey
    {
        profile_next = 1,
        profile_prev = 2,
        toggle_auto = 3,
    }

    /// <summary>Raised on the hotkey thread — marshal to the UI thread before touching app state.</summary>
    public event Action<hotkey>? triggered;

    /// <summary>One registered hotkey: which action, plus its modifier mask and virtual-key code.</summary>
    public sealed record binding(hotkey id, uint mods, uint vk);

    private const uint MOD_NOREPEAT = 0x4000;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const uint PM_NOREMOVE = 0x0000;

    private readonly ILogger<hotkey_service> log;
    private Thread? thread;
    private uint thread_id;
    private IReadOnlyList<binding> bindings = Array.Empty<binding>();

    public hotkey_service(ILogger<hotkey_service> log) => this.log = log;

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
                triggered?.Invoke((hotkey)msg.wParam.ToInt32());
            }
        }

        unregister_all();
    }

    private void register_all()
    {
        foreach (var b in bindings)
        {
            if (b.vk == 0)
            {
                continue; // unset binding
            }
            // MOD_NOREPEAT is a registration concern, not stored in the binding
            if (!RegisterHotKey(IntPtr.Zero, (int)b.id, b.mods | MOD_NOREPEAT, b.vk))
            {
                log.LogWarning(
                    "Hotkey {combo} could not be registered (likely claimed by another app)",
                    hotkey_binding.describe(b.mods, b.vk, english: true));
            }
        }
    }

    private static void unregister_all()
    {
        foreach (var id in Enum.GetValues<hotkey>())
        {
            UnregisterHotKey(IntPtr.Zero, (int)id);
        }
    }

    public void Dispose() => stop();

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
