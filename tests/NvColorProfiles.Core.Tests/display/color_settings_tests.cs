using nv_color_profiles.core.display;

namespace nv_color_profiles.core.tests.display;

public class color_settings_tests
{
    [Fact]
    public void neutral_matches_nvidia_defaults()
    {
        var n = color_settings.neutral;
        Assert.Equal(50, n.brightness);
        Assert.Equal(50, n.contrast);
        Assert.Equal(1.0, n.gamma);
        Assert.Equal(50, n.vibrance);
        Assert.Equal(0, n.hue);
    }

    [Fact]
    public void normalized_clamps_out_of_range_values()
    {
        var s = new color_settings(brightness: 200, contrast: -10, gamma: 9.0, vibrance: 150, hue: 400).normalized();
        Assert.Equal(100, s.brightness);
        Assert.Equal(0, s.contrast);
        Assert.Equal(2.8, s.gamma);
        Assert.Equal(100, s.vibrance);
        Assert.Equal(40, s.hue); // 400 wrapped
    }

    [Fact]
    public void normalized_wraps_negative_hue()
    {
        Assert.Equal(270, new color_settings(50, 50, 1.0, 50, -90).normalized().hue);
    }

    [Fact]
    public void normalized_leaves_valid_values_untouched()
    {
        var s = new color_settings(57, 57, 1.25, 100, 30);
        Assert.Equal(s, s.normalized());
    }
}
