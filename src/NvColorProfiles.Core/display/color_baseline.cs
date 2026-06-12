using Microsoft.Extensions.Logging;
using nv_color_profiles.core.interop;

namespace nv_color_profiles.core.display;

/// <summary>
/// Snapshot of every display's color state (gamma ramp + vibrance + hue) taken at startup, so the
/// app can put things back exactly as it found them on exit or after a crash — never leaving a
/// half-applied gamma ramp behind. A crash-safety net for color state.
/// </summary>
public sealed class color_baseline
{
    private sealed record entry(uint display_id, string gdi_name, ushort[]? ramp, int? vibrance, int? hue);

    private readonly IReadOnlyList<entry> entries;
    private readonly ILogger log;

    private color_baseline(IReadOnlyList<entry> entries, ILogger log)
    {
        this.entries = entries;
        this.log = log;
    }

    /// <summary>Captures the current state of all displays.</summary>
    public static color_baseline capture(
        display_catalog catalog,
        vibrance_control vibrance,
        hue_control hue,
        ILogger log)
    {
        var captured = new List<entry>();
        foreach (var display in catalog.get_displays())
        {
            captured.Add(new entry(
                display.display_id,
                display.gdi_name,
                win_gamma.try_get(display.gdi_name),
                vibrance.get_percent(display.display_id),
                hue.get_angle(display.display_id)));
        }

        log.LogInformation("Captured color baseline for {count} display(s)", captured.Count);
        return new color_baseline(captured, log);
    }

    /// <summary>Writes every captured value back to its display (best-effort, per display).</summary>
    public void restore(vibrance_control vibrance, hue_control hue)
    {
        foreach (var e in entries)
        {
            try
            {
                if (e.ramp is not null)
                {
                    win_gamma.apply(e.gdi_name, e.ramp);
                }
                if (e.vibrance is int v)
                {
                    vibrance.set_percent(e.display_id, v);
                }
                if (e.hue is int h)
                {
                    hue.set_angle(e.display_id, h);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to restore baseline for {name}", e.gdi_name);
            }
        }

        log.LogInformation("Restored color baseline");
    }
}
