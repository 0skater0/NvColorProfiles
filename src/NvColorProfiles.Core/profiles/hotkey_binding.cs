using System.Globalization;
using System.Text;

namespace nv_color_profiles.core.profiles;

/// <summary>
/// A global-hotkey combination as raw Win32 values: <see cref="mods"/> is a MOD_* bitmask
/// (without MOD_NOREPEAT, which is added at registration time) and <see cref="key"/> is a virtual-
/// key code. Stored as opaque ints so the headless core needs no platform APIs; the app maps
/// Avalonia keys into these and registers them.
/// </summary>
public sealed record hotkey_binding
{
    // Win32 modifier flags (RegisterHotKey fsModifiers).
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    public uint mods { get; init; }
    public uint key { get; init; }

    public bool is_set => key != 0;

    /// <summary>Human-readable label, e.g. "Strg+Alt+Bild↓" (German) or "Ctrl+Alt+PgDn" (English).</summary>
    public string display_name(bool english = false) => describe(mods, key, english);

    /// <summary>Formats a modifier mask + virtual-key into a label in the requested language.</summary>
    public static string describe(uint mods, uint key, bool english)
    {
        if (key == 0)
        {
            return "—";
        }
        var parts = new StringBuilder();
        if ((mods & MOD_CONTROL) != 0) parts.Append(english ? "Ctrl+" : "Strg+");
        if ((mods & MOD_ALT) != 0) parts.Append("Alt+");
        if ((mods & MOD_SHIFT) != 0) parts.Append(english ? "Shift+" : "Umschalt+");
        if ((mods & MOD_WIN) != 0) parts.Append("Win+");
        parts.Append(key_name(key, english));
        return parts.ToString();
    }

    private static string key_name(uint vk, bool english) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)('A' + (vk - 0x41))).ToString(),          // A-Z
        >= 0x30 and <= 0x39 => ((char)('0' + (vk - 0x30))).ToString(),          // 0-9
        >= 0x60 and <= 0x69 => "Num " + (vk - 0x60),                            // numpad 0-9
        >= 0x70 and <= 0x87 => "F" + (vk - 0x70 + 1),                           // F1-F24
        0x21 => english ? "PgUp" : "Bild↑",
        0x22 => english ? "PgDn" : "Bild↓",
        0x23 => english ? "End" : "Ende",
        0x24 => english ? "Home" : "Pos1",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        0x2D => english ? "Ins" : "Einfg",
        0x2E => english ? "Del" : "Entf",
        0x20 => english ? "Space" : "Leer",
        _ => "0x" + vk.ToString("X2", CultureInfo.InvariantCulture),
    };
}
