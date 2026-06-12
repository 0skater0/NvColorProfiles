using nv_color_profiles.core.display;
using nv_color_profiles.core.profiles;
using nv_color_profiles.core.rules;

namespace nv_color_profiles.core.tests.profiles;

public class profile_store_import_tests
{
    [Fact]
    public void from_json_with_null_collections_is_sanitized_not_thrown()
    {
        // a hand-edited/truncated import file can carry explicit nulls; System.Text.Json would set
        // the members to null (overriding the `= new()` initializers) and NRE downstream
        var json = """
        {
          "schema_version": 1,
          "profiles": [ { "name": "Gaming", "displays": null } ],
          "rules": null,
          "schedules": null
        }
        """;

        var config = profile_store.from_json(json);

        Assert.NotNull(config);
        Assert.NotNull(config!.rules);
        Assert.NotNull(config.schedules);
        var gaming = config.find_profile("Gaming");
        Assert.NotNull(gaming);
        Assert.NotNull(gaming!.displays);
        Assert.Empty(gaming.displays);
        Assert.NotNull(config.find_profile(app_config.DEFAULT_PROFILE_NAME)); // default still ensured
    }

    [Fact]
    public void from_json_with_null_hotkey_bindings_falls_back_to_defaults()
    {
        var json = """
        { "schema_version": 1, "settings": { "hotkey_next": null, "hotkey_toggle": null } }
        """;

        var config = profile_store.from_json(json);

        Assert.NotNull(config);
        Assert.NotNull(config!.settings.hotkey_next);
        Assert.NotNull(config.settings.hotkey_toggle);
        Assert.Equal("Strg+Alt+Bild↓", config.settings.hotkey_next.display_name());
    }

    [Fact]
    public void per_monitor_overrides_survive_the_json_roundtrip()
    {
        var config = app_config.create_default() with
        {
            profiles = new List<profile>
            {
                profile.uniform(app_config.DEFAULT_PROFILE_NAME, color_settings.neutral, builtin: true),
                new profile
                {
                    name = "Gaming",
                    displays = new()
                    {
                        [profile.ALL_DISPLAYS] = new color_settings(50, 50, 1.0, 60, 0),
                        ["222"] = new color_settings(45, 55, 1.1, 100, 5),
                    },
                },
            },
        };

        var round = profile_store.from_json(profile_store.to_json(config));

        Assert.NotNull(round);
        var gaming = round!.find_profile("Gaming");
        Assert.NotNull(gaming);
        Assert.Equal(2, gaming!.displays.Count);
        Assert.Equal(100, gaming.settings_for(222u).vibrance); // per-monitor override kept
        Assert.Equal(60, gaming.settings_for(999u).vibrance);  // all-displays base kept
    }

    [Fact]
    public void from_json_invalid_returns_null()
        => Assert.Null(profile_store.from_json("{ this is not valid json"));

    [Fact]
    public void to_json_then_from_json_roundtrips_profiles_rules_schedules()
    {
        var config = app_config.create_default() with
        {
            rules = new List<rule> { new() { type = match_type.process, value = "game.exe", profile = "Default" } },
            schedules = new List<schedule_entry> { new() { from = "22:00", to = "06:00", profile = "Default" } },
        };

        var round = profile_store.from_json(profile_store.to_json(config));

        Assert.NotNull(round);
        Assert.Single(round!.rules);
        Assert.Equal("game.exe", round.rules[0].value);
        Assert.Single(round.schedules);
        Assert.Equal("22:00", round.schedules[0].from);
        Assert.Equal("06:00", round.schedules[0].to);
    }
}
