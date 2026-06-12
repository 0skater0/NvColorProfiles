namespace nv_color_profiles.core.display;

/// <summary>Enumerates the currently connected NVIDIA-driven displays.</summary>
public interface display_catalog
{
    IReadOnlyList<nv_display> get_displays();
}
