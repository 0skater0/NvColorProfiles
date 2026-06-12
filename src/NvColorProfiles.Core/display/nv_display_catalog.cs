using Microsoft.Extensions.Logging;
using NvAPIWrapper.Display;
using nv_color_profiles.core.interop;

namespace nv_color_profiles.core.display;

/// <summary>
/// <see cref="display_catalog"/> backed by NvAPI. Maps each NvAPI display to its stable
/// <c>DisplayId</c>, its GDI device name (for gamma application) and a best-effort monitor name.
/// </summary>
public sealed class nv_display_catalog : display_catalog
{
    private readonly nv_session session;
    private readonly ILogger<nv_display_catalog> log;

    public nv_display_catalog(nv_session session, ILogger<nv_display_catalog> log)
    {
        this.session = session;
        this.log = log;
    }

    public IReadOnlyList<nv_display> get_displays()
    {
        if (!session.is_available)
        {
            return Array.Empty<nv_display>();
        }

        var displays = new List<nv_display>();
        try
        {
            var names = win_monitor_names.query();
            foreach (var display in Display.GetDisplays())
            {
                var gdi_name = display.Name;
                var friendly = names.TryGetValue(gdi_name, out var model) && !string.IsNullOrWhiteSpace(model)
                    ? model
                    : win_display.try_get_monitor_name(gdi_name) ?? string.Empty;
                displays.Add(new nv_display(display.DisplayDevice.DisplayId, gdi_name, friendly));
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to enumerate displays");
        }
        return displays;
    }
}
