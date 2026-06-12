using NvAPIWrapper.Display;

namespace nv_color_profiles.core.display;

/// <summary>Resolves an NvAPI <see cref="Display"/> by its stable DisplayId. Shared by the per-display controls.</summary>
internal static class nv_display_lookup
{
    public static Display? by_id(uint display_id)
        => Display.GetDisplays().FirstOrDefault(d => d.DisplayDevice.DisplayId == display_id);
}
