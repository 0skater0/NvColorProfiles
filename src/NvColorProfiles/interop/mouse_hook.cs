using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using nv_color_profiles.core.profiles;

namespace nv_color_profiles.interop;

/// <summary>
/// Windows low-level mouse hook (WH_MOUSE_LL) used to bind XButton1/XButton2 with a modifier as a
/// global hotkey. Bare side-buttons stay pass-through so the browser back/forward gestures keep
/// working. The callback must return within LowLevelHooksTimeout (~300 ms), so it only checks the
/// current modifier state and fires the event — every consumer marshals to the UI thread.
/// </summary>
internal sealed class mouse_hook : IDisposable
{
    /// <summary>Registered mouse-button trigger for one hotkey action. <see cref="id"/> is the
    /// same wparam that <see cref="hotkey_service"/> uses for keyboard bindings, so both paths
    /// dispatch through one lookup.</summary>
    public sealed record mouse_binding(int id, uint mods, uint mouse_button);

    private const int WH_MOUSE_LL = 14;
    private const int HC_ACTION = 0;
    private const int WM_XBUTTONDOWN = 0x020B;

    // GetKeyState / GetAsyncKeyState virtual-key codes.
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;    // Alt
    private const int VK_SHIFT = 0x10;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private readonly ILogger<mouse_hook> log;
    // hold the delegate as a field so the GC does not collect the trampoline while Windows still calls it
    private readonly hook_proc callback;
    private IntPtr hook_handle = IntPtr.Zero;
    private IReadOnlyList<mouse_binding> bindings = Array.Empty<mouse_binding>();
    private Action<int>? on_match;

    public mouse_hook(ILogger<mouse_hook> log)
    {
        this.log = log;
        callback = hook_callback;
    }

    /// <summary>Installs the hook on the calling thread (which must own a message queue).</summary>
    public void install(IReadOnlyList<mouse_binding> bindings, Action<int> on_match)
    {
        this.bindings = bindings;
        this.on_match = on_match;
        if (bindings.Count == 0)
        {
            return; // nothing to watch for — do not add hook overhead to every mouse event
        }
        if (hook_handle != IntPtr.Zero)
        {
            return;
        }
        // module handle can be zero for a managed callback in the same process
        hook_handle = SetWindowsHookEx(WH_MOUSE_LL, callback, IntPtr.Zero, 0);
        if (hook_handle == IntPtr.Zero)
        {
            log.LogWarning("Low-level mouse hook could not be installed (error {err})", Marshal.GetLastWin32Error());
        }
    }

    public void uninstall()
    {
        if (hook_handle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook_handle);
            hook_handle = IntPtr.Zero;
        }
        bindings = Array.Empty<mouse_binding>();
        on_match = null;
    }

    public void Dispose() => uninstall();

    // callback runs on the hooking thread; MUST return quickly or Windows drops the hook silently
    private IntPtr hook_callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HC_ACTION && (int)wParam == WM_XBUTTONDOWN)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var button = (data.mouseData >> 16) & 0xFFFF; // HIWORD → XBUTTON1/2
            var match = find_match(button);
            if (match is not null)
            {
                on_match?.Invoke(match.Value);
                return (IntPtr)1; // swallow so the app underneath does not also receive it
            }
        }
        return CallNextHookEx(hook_handle, code, wParam, lParam);
    }

    private int? find_match(uint mouse_button)
    {
        var mods = current_modifiers();
        // linear scan — small binding list, no allocation, tight upper bound on latency
        for (var i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            if (b.mouse_button == mouse_button && b.mods == mods)
            {
                return b.id;
            }
        }
        return null;
    }

    private static uint current_modifiers()
    {
        uint mods = 0;
        if (is_down(VK_CONTROL)) mods |= hotkey_binding.MOD_CONTROL;
        if (is_down(VK_MENU)) mods |= hotkey_binding.MOD_ALT;
        if (is_down(VK_SHIFT)) mods |= hotkey_binding.MOD_SHIFT;
        if (is_down(VK_LWIN) || is_down(VK_RWIN)) mods |= hotkey_binding.MOD_WIN;
        return mods;
    }

    // GetAsyncKeyState reflects the physical key state at the time of the call, closer to the event than GetKeyState
    private static bool is_down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private delegate IntPtr hook_proc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int pt_x;
        public int pt_y;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, hook_proc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
