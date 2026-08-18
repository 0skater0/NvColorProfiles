using System.Buffers;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using nv_color_profiles.core.interop.nvapi;

namespace nv_color_profiles.core.display;

/// <summary>
/// Applies gamma via the NVAPI display pipeline LUT. This LUT is downstream of game rendering,
/// so the setting survives exclusive-fullscreen present cycles that would wipe the GDI ramp.
/// Not persistent across reboots — that is handled by the app's autostart re-applying the active profile.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class nvapi_gamma_backend
{
    private readonly nv_api_loader loader;
    private readonly ILogger<nvapi_gamma_backend> log;

    public nvapi_gamma_backend(nv_api_loader loader, ILogger<nvapi_gamma_backend> log)
    {
        this.loader = loader;
        this.log = log;
    }

    public bool apply(nv_display display, color_settings settings)
    {
        var s = settings.normalized();

        // Rent instead of allocating 3072 floats per apply — live-slider drag calls this on every tick.
        var ramp = ArrayPool<float>.Shared.Rent(gamma_ramp.NVAPI_RAMP_LENGTH);
        int status;
        try
        {
            var span = ramp.AsSpan(0, gamma_ramp.NVAPI_RAMP_LENGTH);
            gamma_ramp.fill_nvapi_ramp(s.brightness / 100.0, s.contrast / 100.0, s.gamma, span);
            status = nv_gamma_correction.apply(loader, display.display_id, span);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(ramp);
        }
        if (status != 0)
        {
            log.LogWarning(
                "NvAPI SetTargetGammaCorrection failed for display 0x{id:X8}: status {status}",
                display.display_id, status);
            return false;
        }
        return true;
    }

    public bool reset(nv_display display) => apply(display, color_settings.neutral);
}
