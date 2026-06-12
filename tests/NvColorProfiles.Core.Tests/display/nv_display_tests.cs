using nv_color_profiles.core.display;

namespace nv_color_profiles.core.tests.display;

public class nv_display_tests
{
    [Fact]
    public void label_combines_friendly_and_gdi_name()
    {
        var display = new nv_display(0x80061082, @"\\.\DISPLAY1", "LG ULTRAGEAR");
        Assert.Equal(@"LG ULTRAGEAR (\\.\DISPLAY1)", display.label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void label_falls_back_to_gdi_name_when_friendly_missing(string friendly)
    {
        var display = new nv_display(1, @"\\.\DISPLAY2", friendly);
        Assert.Equal(@"\\.\DISPLAY2", display.label);
    }
}
