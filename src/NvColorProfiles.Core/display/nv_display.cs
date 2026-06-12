namespace nv_color_profiles.core.display;

/// <summary>
/// A connected display.
/// <list type="bullet">
///   <item><see cref="display_id"/> — stable NvAPI identifier, used as the config key.</item>
///   <item><see cref="gdi_name"/> — GDI device name ("\\.\DISPLAY1"); what CreateDC /
///         SetDeviceGammaRamp need. May change across reboots, so it is resolved from the id.</item>
///   <item><see cref="friendly_name"/> — best-effort human name for the UI (may be empty).</item>
/// </list>
/// </summary>
public sealed record nv_display(uint display_id, string gdi_name, string friendly_name)
{
    /// <summary>Display label for the UI — friendly name plus GDI name, or just the GDI name.</summary>
    public string label => string.IsNullOrWhiteSpace(friendly_name)
        ? gdi_name
        : $"{friendly_name} ({gdi_name})";
}
