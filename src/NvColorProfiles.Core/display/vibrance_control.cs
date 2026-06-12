using Microsoft.Extensions.Logging;

namespace nv_color_profiles.core.display;

/// <summary>
/// Reads and sets NVIDIA Digital Vibrance per display via NvAPI.
///
/// Observed hardware semantics (RTX 5090): level range 0..100, default 50; NvAPIWrapper's
/// <c>NormalizedLevel</c> is centred on the default (0 = default, +1 = max, -1 = min). We expose a
/// 0..100 percentage (50 = neutral, matching the panel) and map it via (percent - 50) / 50.
/// </summary>
public sealed class vibrance_control
{
    public const int NEUTRAL_PERCENT = 50;

    private readonly nv_session session;
    private readonly ILogger<vibrance_control> log;

    public vibrance_control(nv_session session, ILogger<vibrance_control> log)
    {
        this.session = session;
        this.log = log;
    }

    /// <summary>Current vibrance as a 0..100 percentage, or null if unavailable.</summary>
    public int? get_percent(uint display_id)
    {
        if (!session.is_available)
        {
            return null;
        }

        try
        {
            var display = nv_display_lookup.by_id(display_id);
            return display is null ? null : Math.Clamp(display.DigitalVibranceControl.CurrentLevel, 0, 100);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to read vibrance on display 0x{id:X8}", display_id);
            return null;
        }
    }

    /// <summary>Sets vibrance from a 0..100 percentage (50 = neutral). Returns false on failure.</summary>
    public bool set_percent(uint display_id, int percent)
    {
        if (!session.is_available)
        {
            return false;
        }

        try
        {
            var display = nv_display_lookup.by_id(display_id);
            if (display is null)
            {
                return false;
            }

            display.DigitalVibranceControl.NormalizedLevel = normalized_from_percent(percent);
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to set vibrance on display 0x{id:X8}", display_id);
            return false;
        }
    }

    /// <summary>Maps a 0..100 percentage to NvAPI's default-centred normalized level [-1, 1].</summary>
    internal static double normalized_from_percent(int percent)
        => (Math.Clamp(percent, 0, 100) - NEUTRAL_PERCENT) / 50.0;
}
