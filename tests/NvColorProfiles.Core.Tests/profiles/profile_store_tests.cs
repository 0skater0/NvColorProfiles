using Microsoft.Extensions.Logging.Abstractions;
using nv_color_profiles.core.display;
using nv_color_profiles.core.profiles;

namespace nv_color_profiles.core.tests.profiles;

public sealed class profile_store_tests : IDisposable
{
    private readonly string dir;
    private readonly string config_path;
    private readonly profile_store store;

    public profile_store_tests()
    {
        dir = Path.Combine(Path.GetTempPath(), "nvcp_cfg_" + Guid.NewGuid().ToString("N"));
        config_path = Path.Combine(dir, "config.json");
        store = new profile_store(config_path, NullLogger<profile_store>.Instance);
    }

    [Fact]
    public void load_missing_returns_default_with_default_profile()
    {
        var config = store.load();
        var def = config.find_profile("Default");
        Assert.NotNull(def);
        Assert.True(def!.builtin);
    }

    [Fact]
    public void save_then_load_round_trips_content()
    {
        var gaming = new color_settings(57, 57, 1.25, 100, 0);
        var config = app_config.create_default() with
        {
            settings = new app_settings { autostart = true, active_profile = "Gaming" },
        };
        config.profiles.Add(profile.uniform("Gaming", gaming));

        store.save(config);
        var loaded = store.load();

        Assert.True(loaded.settings.autostart);
        Assert.Equal("Gaming", loaded.settings.active_profile);
        Assert.NotNull(loaded.find_profile("Default"));
        Assert.Equal(gaming, loaded.find_profile("Gaming")!.settings_for(123));
    }

    [Fact]
    public void load_corrupt_backs_up_and_returns_default()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(config_path, "{ this is not valid json ");

        var config = store.load();

        Assert.NotNull(config.find_profile("Default"));
        Assert.True(File.Exists(config_path + ".corrupt"));
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
