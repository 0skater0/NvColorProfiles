// SPDX-License-Identifier: LGPL-3.0-or-later
//
// The calculate_lut gamma-curve formula below is derived from WindowsDisplayAPI
// (c) Soroush Falahati, https://github.com/falahati/WindowsDisplayAPI, licensed under the
// GNU LGPL-3.0. That makes the curve math in this file a derivative work covered by the
// LGPL-3.0 (full text in COPYING.LESSER / COPYING). The rest of the project is MIT.

namespace nv_color_profiles.core.display;

/// <summary>
/// A monitor gamma lookup table (256 entries per channel) computed from brightness, contrast and
/// gamma — the three NVIDIA "Desktop Color Settings" sliders that are applied via the gamma ramp.
///
/// The curve formula is the proven NVIDIA-Control-Panel-matching algorithm from falahati's
/// WindowsDisplayAPI (LGPL-3.0; see the license note at the top of this file), reimplemented here
/// to keep the dependency surface small and the math unit-testable. All channels share one curve
/// (R=G=B); per-channel control is out of scope.
/// </summary>
public sealed class gamma_ramp
{
    public const int DATA_POINTS = 256;

    private readonly ushort[] channel;

    private gamma_ramp(ushort[] channel) => this.channel = channel;

    public IReadOnlyList<ushort> values => channel;

    /// <summary>
    /// Builds a ramp from normalized inputs: <paramref name="brightness"/> and
    /// <paramref name="contrast"/> in [0,1] (0.5 = neutral), <paramref name="gamma"/> in
    /// [0.4,2.8] (1.0 = neutral). 0.5 / 0.5 / 1.0 yields the identity ramp.
    /// </summary>
    public static gamma_ramp from_settings(double brightness, double contrast, double gamma)
        => new(calculate_lut(brightness, contrast, gamma));

    /// <summary>Flattened R|G|B buffer (768 entries) as expected by SetDeviceGammaRamp.</summary>
    public ushort[] to_rgb_buffer()
    {
        var buffer = new ushort[DATA_POINTS * 3];
        Array.Copy(channel, 0, buffer, 0, DATA_POINTS);
        Array.Copy(channel, 0, buffer, DATA_POINTS, DATA_POINTS);
        Array.Copy(channel, 0, buffer, DATA_POINTS * 2, DATA_POINTS);
        return buffer;
    }

    private static ushort[] calculate_lut(double brightness, double contrast, double gamma)
    {
        // Match the NVIDIA panel behaviour (algorithm: falahati/WindowsDisplayAPI, LGPL-3.0).
        gamma = Math.Clamp(gamma, 0.4, 2.8);
        contrast = (Math.Clamp(contrast, 0, 1) - 0.5) * 2;   // -> [-1, 1]
        brightness = (Math.Clamp(brightness, 0, 1) - 0.5) * 2; // -> [-1, 1]

        var offset = contrast > 0 ? contrast * -25.4 : contrast * -32;
        var range = DATA_POINTS - 1 + offset * 2;
        offset += brightness * (range / 5);

        var result = new ushort[DATA_POINTS];
        for (var i = 0; i < result.Length; i++)
        {
            var factor = (i + offset) / range;
            factor = Math.Max(factor, 0); // avoid NaN from pow() on a negative base
            factor = Math.Pow(factor, 1.0 / gamma);
            factor = Math.Clamp(factor, 0, 1);
            result[i] = (ushort)Math.Round(factor * ushort.MaxValue);
        }
        return result;
    }
}
