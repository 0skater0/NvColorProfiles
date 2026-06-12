using nv_color_profiles.core.display;

namespace nv_color_profiles.core.tests.display;

public class gamma_ramp_tests
{
    private static IReadOnlyList<ushort> neutral() => gamma_ramp.from_settings(0.5, 0.5, 1.0).values;

    [Fact]
    public void neutral_settings_yield_identity_ramp()
    {
        var ramp = neutral();

        Assert.Equal(256, ramp.Count);
        Assert.Equal(0, ramp[0]);
        Assert.Equal(ushort.MaxValue, ramp[255]);

        // identity: value == round(i/255 * 65535)
        for (var i = 0; i < 256; i++)
        {
            var expected = (ushort)Math.Round(i / 255.0 * ushort.MaxValue);
            Assert.Equal(expected, ramp[i]);
        }
    }

    [Fact]
    public void ramp_is_monotonic_non_decreasing()
    {
        var ramp = neutral();
        for (var i = 1; i < ramp.Count; i++)
        {
            Assert.True(ramp[i] >= ramp[i - 1]);
        }
    }

    [Fact]
    public void higher_brightness_lifts_midtone()
    {
        var darker = gamma_ramp.from_settings(0.5, 0.5, 1.0).values[128];
        var brighter = gamma_ramp.from_settings(0.75, 0.5, 1.0).values[128];
        Assert.True(brighter > darker);
    }

    [Fact]
    public void higher_gamma_lifts_midtone_without_touching_endpoints()
    {
        var identity = gamma_ramp.from_settings(0.5, 0.5, 1.0).values;
        var raised = gamma_ramp.from_settings(0.5, 0.5, 2.0).values;

        Assert.True(raised[128] > identity[128]);
        Assert.Equal(identity[0], raised[0]);       // black stays black
        Assert.Equal(identity[255], raised[255]);   // white stays white
    }

    [Fact]
    public void gamma_upper_bound_is_clamped()
    {
        var clamped = gamma_ramp.from_settings(0.5, 0.5, 2.8).values;
        var beyond = gamma_ramp.from_settings(0.5, 0.5, 9.0).values;
        Assert.Equal(clamped, beyond);
    }

    [Fact]
    public void gamma_lower_bound_is_clamped()
    {
        var clamped = gamma_ramp.from_settings(0.5, 0.5, 0.4).values;
        var below = gamma_ramp.from_settings(0.5, 0.5, 0.1).values;
        Assert.Equal(clamped, below);
    }

    [Fact]
    public void rgb_buffer_repeats_channel_three_times()
    {
        var ramp = gamma_ramp.from_settings(0.6, 0.4, 1.2);
        var buffer = ramp.to_rgb_buffer();

        Assert.Equal(256 * 3, buffer.Length);
        for (var i = 0; i < 256; i++)
        {
            Assert.Equal(ramp.values[i], buffer[i]);
            Assert.Equal(ramp.values[i], buffer[i + 256]);
            Assert.Equal(ramp.values[i], buffer[i + 512]);
        }
    }
}
