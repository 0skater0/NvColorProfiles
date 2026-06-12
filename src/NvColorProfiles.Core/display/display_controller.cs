namespace nv_color_profiles.core.display;

/// <summary>Applies and reads back the full set of color adjustments for a display.</summary>
public interface display_controller
{
    /// <summary>Applies all five adjustments to the display (best-effort per control).</summary>
    void apply(color_settings settings, nv_display display);

    /// <summary>
    /// Reads the current hardware state. Vibrance and hue are read exactly; brightness, contrast
    /// and gamma cannot be recovered from the gamma ramp (the LUT is not invertible) and come back
    /// as neutral. Callers that need exact b/c/g should track the last applied values themselves.
    /// </summary>
    color_settings read_current(nv_display display);
}
