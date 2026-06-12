using nv_color_profiles.core.display;
using nv_color_profiles.core.profiles;

namespace nv_color_profiles.core.tests.profiles;

public class profile_settings_for_tests
{
    private static readonly color_settings warm = new(40, 60, 1.2, 70, 10);
    private static readonly color_settings punchy = new(50, 50, 1.0, 100, 0);

    [Fact]
    public void uniform_profile_applies_to_every_display()
    {
        var p = profile.uniform("Uniform", warm);
        Assert.Equal(warm, p.settings_for(111u));
        Assert.Equal(warm, p.settings_for(222u));
    }

    [Fact]
    public void per_monitor_override_wins_for_that_display_only()
    {
        var p = new profile
        {
            name = "Mixed",
            displays = new() { [profile.ALL_DISPLAYS] = warm, ["222"] = punchy },
        };
        Assert.Equal(punchy, p.settings_for(222u)); // its own values
        Assert.Equal(warm, p.settings_for(111u));   // follows the all-displays base
    }

    [Fact]
    public void display_without_entry_and_no_wildcard_is_neutral()
    {
        var p = new profile { name = "OnlyMon", displays = new() { ["222"] = warm } };
        Assert.Equal(color_settings.neutral, p.settings_for(111u));
        Assert.Equal(warm, p.settings_for(222u));
    }

    [Fact]
    public void string_overload_resolves_the_wildcard_sentinel()
        => Assert.Equal(warm, profile.uniform("Uniform", warm).settings_for(profile.ALL_DISPLAYS));
}
