using nv_color_profiles.core.display;

namespace nv_color_profiles.core.tests.display;

public class hue_control_tests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(359, 359)]
    [InlineData(360, 0)]
    [InlineData(720, 0)]
    [InlineData(400, 40)]
    [InlineData(-1, 359)]
    [InlineData(-90, 270)]
    [InlineData(-360, 0)]
    public void normalize_angle_wraps_into_0_359(int input, int expected)
    {
        Assert.Equal(expected, hue_control.normalize_angle(input));
    }
}
