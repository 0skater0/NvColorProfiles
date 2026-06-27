using NvAPIWrapper.Display;

namespace nv_color_profiles.core.display;

/// <summary>
/// Resolves an NvAPI <see cref="Display"/> by its stable DisplayId. The handle map is cached: each
/// Display.GetDisplays() is a full driver enumeration, and resolving it per vibrance/hue read and
/// write made every profile apply do (1 + 2 * display-count) enumerations on the UI thread. The cache
/// is rebuilt lazily and must be dropped on any display-topology change (see
/// <see cref="nv_display_catalog.invalidate"/>).
/// </summary>
internal static class nv_display_lookup
{
    private static readonly object gate = new();
    private static Dictionary<uint, Display>? by_display_id;

    public static Display? by_id(uint display_id)
    {
        lock (gate)
        {
            by_display_id ??= build();
            return by_display_id.TryGetValue(display_id, out var display) ? display : null;
        }
    }

    /// <summary>Drops the cached handles so the next lookup re-enumerates.</summary>
    public static void invalidate()
    {
        lock (gate)
        {
            by_display_id = null;
        }
    }

    private static Dictionary<uint, Display> build()
    {
        var map = new Dictionary<uint, Display>();
        foreach (var display in Display.GetDisplays())
        {
            map[display.DisplayDevice.DisplayId] = display;
        }
        return map;
    }
}
