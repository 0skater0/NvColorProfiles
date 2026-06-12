using Avalonia.Input;
using nv_color_profiles.core.profiles;

namespace nv_color_profiles.interop;

/// <summary>
/// Maps Avalonia key input into the raw Win32 modifier mask + virtual-key code used by
/// RegisterHotKey. The VK numbers come from the offset within each contiguous Avalonia key range,
/// so they hold regardless of the enum's base value.
/// </summary>
internal static class hotkey_keys
{
    private static readonly Dictionary<Key, uint> key_to_vk = build_map();

    public static bool try_map(Key key, out uint vk) => key_to_vk.TryGetValue(key, out vk);

    public static uint to_win_mods(KeyModifiers modifiers)
    {
        uint mods = 0;
        if (modifiers.HasFlag(KeyModifiers.Control)) mods |= hotkey_binding.MOD_CONTROL;
        if (modifiers.HasFlag(KeyModifiers.Alt)) mods |= hotkey_binding.MOD_ALT;
        if (modifiers.HasFlag(KeyModifiers.Shift)) mods |= hotkey_binding.MOD_SHIFT;
        if (modifiers.HasFlag(KeyModifiers.Meta)) mods |= hotkey_binding.MOD_WIN;
        return mods;
    }

    /// <summary>A global hotkey needs a non-shift modifier, else it would swallow the bare key.</summary>
    public static bool has_required_modifier(uint mods)
        => (mods & (hotkey_binding.MOD_CONTROL | hotkey_binding.MOD_ALT | hotkey_binding.MOD_WIN)) != 0;

    public static bool is_modifier_key(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private static Dictionary<Key, uint> build_map()
    {
        var map = new Dictionary<Key, uint>();
        for (var v = (int)Key.A; v <= (int)Key.Z; v++) map[(Key)v] = (uint)(0x41 + (v - (int)Key.A));
        for (var v = (int)Key.D0; v <= (int)Key.D9; v++) map[(Key)v] = (uint)(0x30 + (v - (int)Key.D0));
        for (var v = (int)Key.NumPad0; v <= (int)Key.NumPad9; v++) map[(Key)v] = (uint)(0x60 + (v - (int)Key.NumPad0));
        for (var v = (int)Key.F1; v <= (int)Key.F24; v++) map[(Key)v] = (uint)(0x70 + (v - (int)Key.F1));
        map[Key.Left] = 0x25;
        map[Key.Up] = 0x26;
        map[Key.Right] = 0x27;
        map[Key.Down] = 0x28;
        map[Key.PageUp] = 0x21;
        map[Key.PageDown] = 0x22;
        map[Key.End] = 0x23;
        map[Key.Home] = 0x24;
        map[Key.Insert] = 0x2D;
        map[Key.Delete] = 0x2E;
        map[Key.Space] = 0x20;
        return map;
    }
}
