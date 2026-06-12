using nv_color_profiles.core;

namespace nv_color_profiles.core.tests;

public class app_info_tests
{
    [Fact]
    public void app_name_is_set()
    {
        Assert.Equal("NvColorProfiles", app_info.APP_NAME);
    }
}
