using nv_color_profiles.core.display;
using nv_color_profiles.core.profiles;
using nv_color_profiles.core.rules;

namespace nv_color_profiles.core.tests.profiles;

/// <summary>
/// Covers the pure-function rename cascade in <see cref="app_config.with_profile_renamed"/>. The
/// UI wiring in <c>settings_window.profiles.cs</c> just delegates to this method, so a green suite
/// here also protects the on-rename handler against silent regressions.
/// </summary>
public sealed class app_config_rename_tests
{
    private static profile mk_profile(string name) => profile.uniform(name, color_settings.neutral);

    [Fact]
    public void cascades_to_active_fallback_rules_and_schedules()
    {
        var cfg = new app_config
        {
            settings = new app_settings { active_profile = "Foo", fallback_profile = "Foo" },
            profiles = { mk_profile("Foo"), mk_profile("Other") },
            rules = { new rule { profile = "foo", value = "game.exe", type = match_type.process } },
            schedules = { new schedule_entry { profile = "FOO", from = "22:00", to = "06:00" } },
        };

        var result = cfg.with_profile_renamed("Foo", "Bar");

        Assert.Equal("Bar", result.settings.active_profile);
        Assert.Equal("Bar", result.settings.fallback_profile);
        Assert.Equal("Bar", result.rules[0].profile);
        Assert.Equal("Bar", result.schedules[0].profile);
        Assert.Contains(result.profiles, p => p.name == "Bar");
        Assert.DoesNotContain(result.profiles, p => p.name == "Foo");
    }

    [Fact]
    public void leaves_unrelated_references_alone()
    {
        var cfg = new app_config
        {
            settings = new app_settings { active_profile = "Other", fallback_profile = "Other" },
            profiles = { mk_profile("Foo"), mk_profile("Other") },
            rules = { new rule { profile = "Other", value = "game.exe", type = match_type.process } },
            schedules = { new schedule_entry { profile = "Other" } },
        };

        var result = cfg.with_profile_renamed("Foo", "Bar");

        Assert.Equal("Other", result.settings.active_profile);
        Assert.Equal("Other", result.settings.fallback_profile);
        Assert.Equal("Other", result.rules[0].profile);
        Assert.Equal("Other", result.schedules[0].profile);
    }

    [Fact]
    public void case_insensitive_match_on_every_reference()
    {
        var cfg = new app_config
        {
            settings = new app_settings { active_profile = "FOO", fallback_profile = "foo" },
            profiles = { mk_profile("Foo") },
            rules = { new rule { profile = "fOo", value = "x.exe", type = match_type.process } },
            schedules = { new schedule_entry { profile = "FoO" } },
        };

        var result = cfg.with_profile_renamed("foo", "Bar");

        Assert.Equal("Bar", result.settings.active_profile);
        Assert.Equal("Bar", result.settings.fallback_profile);
        Assert.Equal("Bar", result.rules[0].profile);
        Assert.Equal("Bar", result.schedules[0].profile);
        Assert.Contains(result.profiles, p => p.name == "Bar");
    }

    [Fact]
    public void identical_names_are_a_no_op()
    {
        var cfg = new app_config { profiles = { mk_profile("Foo") } };
        var result = cfg.with_profile_renamed("Foo", "Foo");
        Assert.Same(cfg, result);
    }

    [Fact]
    public void differing_names_return_a_new_instance_even_when_nothing_matched()
    {
        var cfg = new app_config { profiles = { mk_profile("Other") } };

        var result = cfg.with_profile_renamed("Foo", "Bar");

        Assert.NotSame(cfg, result);
        Assert.Contains(result.profiles, p => p.name == "Other");
        Assert.DoesNotContain(result.profiles, p => p.name == "Foo");
        Assert.DoesNotContain(result.profiles, p => p.name == "Bar");
    }

    [Fact]
    public void multiple_rules_and_schedules_all_get_rewritten()
    {
        var cfg = new app_config
        {
            profiles = { mk_profile("Foo"), mk_profile("Other") },
            rules =
            {
                new rule { profile = "Foo", value = "a.exe", type = match_type.process },
                new rule { profile = "Other", value = "b.exe", type = match_type.process },
                new rule { profile = "foo", value = "c.exe", type = match_type.process },
            },
            schedules =
            {
                new schedule_entry { profile = "Foo" },
                new schedule_entry { profile = "Other" },
                new schedule_entry { profile = "FOO" },
            },
        };

        var result = cfg.with_profile_renamed("Foo", "Bar");

        Assert.Equal(new[] { "Bar", "Other", "Bar" }, result.rules.Select(r => r.profile));
        Assert.Equal(new[] { "Bar", "Other", "Bar" }, result.schedules.Select(s => s.profile));
    }
}
