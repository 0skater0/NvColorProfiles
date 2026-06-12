using Microsoft.Extensions.Logging;

namespace nv_color_profiles.core.display;

/// <summary>
/// Reads and sets the NVIDIA desktop hue angle (0..359°, 0 = neutral) per display via NvAPI's
/// HUEControl. Mirrors the panel's "Farbton" slider.
/// </summary>
public sealed class hue_control
{
    public const int NEUTRAL_ANGLE = 0;

    private readonly nv_session session;
    private readonly ILogger<hue_control> log;

    public hue_control(nv_session session, ILogger<hue_control> log)
    {
        this.session = session;
        this.log = log;
    }

    /// <summary>Current hue angle in [0, 359], or null if unavailable.</summary>
    public int? get_angle(uint display_id)
    {
        if (!session.is_available)
        {
            return null;
        }

        try
        {
            var display = nv_display_lookup.by_id(display_id);
            return display is null ? null : normalize_angle(display.HUEControl.CurrentAngle);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to read hue on display 0x{id:X8}", display_id);
            return null;
        }
    }

    /// <summary>Sets the hue angle (any integer; wrapped into [0, 359]). Returns false on failure.</summary>
    public bool set_angle(uint display_id, int angle)
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

            display.HUEControl.CurrentAngle = normalize_angle(angle);
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to set hue on display 0x{id:X8}", display_id);
            return false;
        }
    }

    /// <summary>Wraps any integer angle into the [0, 359] range (handles negatives and overflow).</summary>
    internal static int normalize_angle(int angle) => ((angle % 360) + 360) % 360;
}
