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

    [Fact]
    public void nvapi_ramp_has_expected_shape()
    {
        var ramp = gamma_ramp.to_nvapi_ramp(0.5, 0.5, 1.0);
        Assert.Equal(1024 * 3, ramp.Length);
    }

    [Fact]
    public void nvapi_ramp_identity_at_endpoints()
    {
        var ramp = gamma_ramp.to_nvapi_ramp(0.5, 0.5, 1.0);
        Assert.Equal(0.0f, ramp[0], precision: 3);        // R[0]
        Assert.Equal(0.0f, ramp[1], precision: 3);        // G[0]
        Assert.Equal(0.0f, ramp[2], precision: 3);        // B[0]
        Assert.Equal(1.0f, ramp[(1024 - 1) * 3 + 0], precision: 3);
        Assert.Equal(1.0f, ramp[(1024 - 1) * 3 + 1], precision: 3);
        Assert.Equal(1.0f, ramp[(1024 - 1) * 3 + 2], precision: 3);
    }

    [Fact]
    public void nvapi_ramp_is_interleaved_rgb_per_index()
    {
        // per-channel writes (r=g=b) mean interleaved triplets must be identical within each index
        var ramp = gamma_ramp.to_nvapi_ramp(0.6, 0.55, 1.3);
        for (var i = 0; i < 1024; i++)
        {
            Assert.Equal(ramp[i * 3 + 0], ramp[i * 3 + 1]);
            Assert.Equal(ramp[i * 3 + 0], ramp[i * 3 + 2]);
        }
    }

    [Fact]
    public void nvapi_ramp_monotonic_non_decreasing_for_neutral()
    {
        var ramp = gamma_ramp.to_nvapi_ramp(0.5, 0.5, 1.0);
        for (var i = 1; i < 1024; i++)
        {
            Assert.True(ramp[i * 3] >= ramp[(i - 1) * 3]);
        }
    }

    [Fact]
    public void nvapi_ramp_higher_gamma_lifts_midtone()
    {
        var neutral = gamma_ramp.to_nvapi_ramp(0.5, 0.5, 1.0);
        var raised = gamma_ramp.to_nvapi_ramp(0.5, 0.5, 2.0);
        Assert.True(raised[512 * 3] > neutral[512 * 3]);
    }

    [Fact]
    public void fill_nvapi_ramp_matches_allocating_variant()
    {
        var expected = gamma_ramp.to_nvapi_ramp(0.55, 0.6, 1.1);
        var buffer = new float[gamma_ramp.NVAPI_RAMP_LENGTH];
        gamma_ramp.fill_nvapi_ramp(0.55, 0.6, 1.1, buffer);
        Assert.Equal(expected, buffer);
    }

    [Fact]
    public void fill_nvapi_ramp_accepts_oversized_buffer()
    {
        var oversized = new float[gamma_ramp.NVAPI_RAMP_LENGTH + 128];
        gamma_ramp.fill_nvapi_ramp(0.5, 0.5, 1.0, oversized);
        var expected = gamma_ramp.to_nvapi_ramp(0.5, 0.5, 1.0);
        for (var i = 0; i < gamma_ramp.NVAPI_RAMP_LENGTH; i++)
        {
            Assert.Equal(expected[i], oversized[i]);
        }
    }

    [Fact]
    public void fill_nvapi_ramp_rejects_undersized_buffer()
    {
        var too_small = new float[gamma_ramp.NVAPI_RAMP_LENGTH - 1];
        Assert.Throws<ArgumentException>(() => gamma_ramp.fill_nvapi_ramp(0.5, 0.5, 1.0, too_small));
    }
}
