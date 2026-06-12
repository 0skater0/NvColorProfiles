using nv_color_profiles.core.display;
using nv_color_profiles.core.profiles;

namespace nv_color_profiles.core.tests.profiles;

public sealed class profile_tests
{
    [Fact]
    public void settings_for_prefers_exact_display_key()
    {
        var exact = new color_settings(60, 60, 1.2, 70, 10);
        var p = new profile
        {
            name = "p",
            displays =
            {
                [profile.ALL_DISPLAYS] = color_settings.neutral,
                ["42"] = exact,
            },
        };

        Assert.Equal(exact, p.settings_for(42));
    }

    [Fact]
    public void settings_for_falls_back_to_wildcard()
    {
        var all = new color_settings(55, 55, 1.1, 60, 0);
        var p = profile.uniform("p", all);
        Assert.Equal(all, p.settings_for(999));
    }

    [Fact]
    public void settings_for_falls_back_to_neutral_when_empty()
    {
        var p = new profile { name = "empty" };
        Assert.Equal(color_settings.neutral, p.settings_for(1));
    }
}
