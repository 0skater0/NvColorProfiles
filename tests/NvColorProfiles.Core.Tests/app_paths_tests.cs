using nv_color_profiles.core;

namespace nv_color_profiles.core.tests;

public class app_paths_tests
{
    [Fact]
    public void config_and_log_paths_are_anchored_to_base_dir()
    {
        Assert.False(string.IsNullOrWhiteSpace(app_paths.base_dir));
        Assert.StartsWith(app_paths.base_dir, app_paths.log_dir);
        Assert.StartsWith(app_paths.base_dir, app_paths.config_file);
        Assert.StartsWith(app_paths.log_dir, app_paths.log_file);
    }

    [Fact]
    public void file_names_match_constants()
    {
        Assert.EndsWith(app_paths.CONFIG_FILE_NAME, app_paths.config_file);
        Assert.EndsWith(app_paths.LOG_FILE_NAME, app_paths.log_file);
    }
}
