using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace nv_color_profiles.core.display;

/// <summary>
/// Default controller: b/c/g via the NVAPI gamma backend (may be null when NvAPI init failed),
/// vibrance/hue via NvAPI. Each control is applied independently; a failure never blocks the others.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class nv_display_controller : display_controller
{
    private readonly nvapi_gamma_backend? gamma;
    private readonly vibrance_control vibrance;
    private readonly hue_control hue;
    private readonly ILogger<nv_display_controller> log;

    public nv_display_controller(
        nvapi_gamma_backend? gamma,
        vibrance_control vibrance,
        hue_control hue,
        ILogger<nv_display_controller> log)
    {
        this.gamma = gamma;
        this.vibrance = vibrance;
        this.hue = hue;
        this.log = log;
    }

    public void apply(color_settings settings, nv_display display)
    {
        var s = settings.normalized();

        gamma?.apply(display, s);
        vibrance.set_percent(display.display_id, s.vibrance);
        hue.set_angle(display.display_id, s.hue);

        log.LogDebug(
            "Applied to {name}: b={b} c={c} g={g} dv={dv} hue={hue} gamma={gamma}",
            display.gdi_name, s.brightness, s.contrast, s.gamma, s.vibrance, s.hue,
            gamma is null ? "unavailable" : "nvapi");
    }

    public color_settings read_current(nv_display display)
    {
        var dv = vibrance.get_percent(display.display_id) ?? vibrance_control.NEUTRAL_PERCENT;
        var h = hue.get_angle(display.display_id) ?? hue_control.NEUTRAL_ANGLE;

        // brightness/contrast/gamma are not recoverable from the NVAPI LUT — report neutral.
        return color_settings.neutral with { vibrance = dv, hue = h };
    }
}
