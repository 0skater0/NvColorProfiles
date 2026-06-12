using nv_color_profiles.core.profiles;

namespace nv_color_profiles.core.tests.profiles;

public class hotkey_binding_tests
{
    [Fact]
    public void describe_formats_modifiers_and_keys()
    {
        Assert.Equal("Strg+Alt+Bild↓", hotkey_binding.describe(0x0003, 0x22, english: false));   // Ctrl+Alt + PageDown
        Assert.Equal("Strg+Alt+Bild↑", hotkey_binding.describe(0x0003, 0x21, english: false));   // Ctrl+Alt + PageUp
        Assert.Equal("Strg+Alt+A", hotkey_binding.describe(0x0003, 0x41, english: false));       // Ctrl+Alt + 'A'
        Assert.Equal("Win+F5", hotkey_binding.describe(hotkey_binding.MOD_WIN, 0x74, english: false));
        Assert.Equal(
            "Strg+Umschalt+5",
            hotkey_binding.describe(hotkey_binding.MOD_CONTROL | hotkey_binding.MOD_SHIFT, 0x35, english: false));
    }

    [Fact]
    public void describe_english_uses_english_modifier_and_key_names()
    {
        Assert.Equal("Ctrl+Alt+PgDn", hotkey_binding.describe(0x0003, 0x22, english: true));
        Assert.Equal("Ctrl+Alt+PgUp", hotkey_binding.describe(0x0003, 0x21, english: true));
        Assert.Equal(
            "Ctrl+Shift+Del",
            hotkey_binding.describe(hotkey_binding.MOD_CONTROL | hotkey_binding.MOD_SHIFT, 0x2E, english: true));
    }

    [Fact]
    public void describe_unset_key_is_dash()
        => Assert.Equal("—", hotkey_binding.describe(0x0003, 0, english: false));

    [Fact]
    public void default_hotkeys_match_the_original_fixed_combos()
    {
        var s = new app_settings();
        Assert.Equal("Strg+Alt+Bild↓", s.hotkey_next.display_name());
        Assert.Equal("Strg+Alt+Bild↑", s.hotkey_prev.display_name());
        Assert.Equal("Strg+Alt+A", s.hotkey_toggle.display_name());
        Assert.True(s.hotkey_next.is_set);
    }
}
