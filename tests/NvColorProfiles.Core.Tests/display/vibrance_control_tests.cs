using nv_color_profiles.core.display;

namespace nv_color_profiles.core.tests.display;

public class vibrance_control_tests
{
    [Theory]
    [InlineData(50, 0.0)]    // neutral / default
    [InlineData(100, 1.0)]   // max
    [InlineData(0, -1.0)]    // min
    [InlineData(75, 0.5)]
    [InlineData(25, -0.5)]
    public void normalized_from_percent_maps_around_default(int percent, double expected)
    {
        Assert.Equal(expected, vibrance_control.normalized_from_percent(percent), precision: 6);
    }

    [Theory]
    [InlineData(-20, -1.0)]   // clamped to 0%
    [InlineData(150, 1.0)]    // clamped to 100%
    public void normalized_from_percent_clamps_out_of_range(int percent, double expected)
    {
        Assert.Equal(expected, vibrance_control.normalized_from_percent(percent), precision: 6);
    }
}
