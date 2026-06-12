using Microsoft.Extensions.Logging.Abstractions;
using nv_color_profiles.core.display;
using nv_color_profiles.core.profiles;

namespace nv_color_profiles.core.tests.profiles;

public sealed class profile_service_tests
{
    private sealed class fake_catalog(IReadOnlyList<nv_display> displays) : display_catalog
    {
        public IReadOnlyList<nv_display> get_displays() => displays;
    }

    private sealed class recording_controller : display_controller
    {
        public List<(color_settings settings, nv_display display)> applied = new();
        public void apply(color_settings settings, nv_display display) => applied.Add((settings, display));
        public color_settings read_current(nv_display display) => color_settings.neutral;
    }

    private static nv_display display(uint id, string name) => new(id, name, "");

    [Fact]
    public void apply_uniform_profile_pushes_same_settings_to_every_display()
    {
        var displays = new[] { display(1, @"\\.\DISPLAY1"), display(2, @"\\.\DISPLAY2") };
        var controller = new recording_controller();
        var service = new profile_service(new fake_catalog(displays), controller, NullLogger<profile_service>.Instance);

        var gaming = new color_settings(57, 57, 1.25, 100, 0);
        service.apply(profile.uniform("Gaming", gaming));

        Assert.Equal(2, controller.applied.Count);
        Assert.All(controller.applied, a => Assert.Equal(gaming, a.settings));
        Assert.Equal("Gaming", service.active_profile_name);
    }

    [Fact]
    public void apply_resolves_per_display_settings()
    {
        var displays = new[] { display(1, @"\\.\DISPLAY1"), display(2, @"\\.\DISPLAY2") };
        var controller = new recording_controller();
        var service = new profile_service(new fake_catalog(displays), controller, NullLogger<profile_service>.Instance);

        var special = new color_settings(60, 60, 1.2, 80, 15);
        var p = new profile
        {
            name = "Mixed",
            displays =
            {
                [profile.ALL_DISPLAYS] = color_settings.neutral,
                ["2"] = special,
            },
        };

        service.apply(p);

        Assert.Equal(color_settings.neutral, controller.applied.Single(a => a.display.display_id == 1).settings);
        Assert.Equal(special, controller.applied.Single(a => a.display.display_id == 2).settings);
    }

    [Fact]
    public void active_profile_is_null_before_first_apply()
    {
        var service = new profile_service(new fake_catalog([]), new recording_controller(), NullLogger<profile_service>.Instance);
        Assert.Null(service.active_profile_name);
    }
}
