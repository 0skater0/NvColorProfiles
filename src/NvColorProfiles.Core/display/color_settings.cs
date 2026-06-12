namespace nv_color_profiles.core.display;

/// <summary>
/// The five output adjustments applied to one display, in UI units:
/// brightness/contrast/vibrance as 0..100 (50 = neutral), gamma 0.4..2.8 (1.0 = neutral),
/// hue 0..359° (0 = neutral). This is the value object profiles are built from.
/// </summary>
public sealed record color_settings(int brightness, int contrast, double gamma, int vibrance, int hue)
{
    /// <summary>NVIDIA-neutral defaults (no visible change).</summary>
    public static color_settings neutral { get; } = new(50, 50, 1.0, 50, 0);

    /// <summary>Returns a copy with every value clamped/wrapped into its valid range.</summary>
    public color_settings normalized() => new(
        Math.Clamp(brightness, 0, 100),
        Math.Clamp(contrast, 0, 100),
        Math.Clamp(gamma, 0.4, 2.8),
        Math.Clamp(vibrance, 0, 100),
        ((hue % 360) + 360) % 360);
}
