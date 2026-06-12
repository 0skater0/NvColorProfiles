using Microsoft.Extensions.Logging;
using nv_color_profiles.core.interop;

namespace nv_color_profiles.core.display;

/// <summary>
/// Default <see cref="display_controller"/>: brightness/contrast/gamma via the GDI gamma ramp,
/// vibrance and hue via NvAPI. Each adjustment is applied independently and best-effort — one
/// failing control never blocks the others.
/// </summary>
public sealed class nv_display_controller : display_controller
{
    private readonly vibrance_control vibrance;
    private readonly hue_control hue;
    private readonly ILogger<nv_display_controller> log;

    public nv_display_controller(vibrance_control vibrance, hue_control hue, ILogger<nv_display_controller> log)
    {
        this.vibrance = vibrance;
        this.hue = hue;
        this.log = log;
    }

    public void apply(color_settings settings, nv_display display)
    {
        var s = settings.normalized();

        var ramp = gamma_ramp.from_settings(s.brightness / 100.0, s.contrast / 100.0, s.gamma);
        if (!win_gamma.apply(display.gdi_name, ramp.to_rgb_buffer()))
        {
            log.LogWarning("Gamma apply failed for {name}", display.gdi_name);
        }

        vibrance.set_percent(display.display_id, s.vibrance);
        hue.set_angle(display.display_id, s.hue);

        log.LogDebug(
            "Applied to {name}: b={b} c={c} g={g} dv={dv} hue={hue}",
            display.gdi_name, s.brightness, s.contrast, s.gamma, s.vibrance, s.hue);
    }

    public color_settings read_current(nv_display display)
    {
        var dv = vibrance.get_percent(display.display_id) ?? vibrance_control.NEUTRAL_PERCENT;
        var h = hue.get_angle(display.display_id) ?? hue_control.NEUTRAL_ANGLE;

        // brightness/contrast/gamma are not recoverable from the gamma ramp — report neutral.
        return color_settings.neutral with { vibrance = dv, hue = h };
    }
}
