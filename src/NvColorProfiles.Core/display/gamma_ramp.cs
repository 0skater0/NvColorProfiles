// SPDX-License-Identifier: LGPL-3.0-or-later
//
// The calculate_lut gamma-curve formula below is derived from WindowsDisplayAPI
// (c) Soroush Falahati, https://github.com/falahati/WindowsDisplayAPI, licensed under the
// GNU LGPL-3.0. That makes the curve math in this file a derivative work covered by the
// LGPL-3.0 (full text in COPYING.LESSER / COPYING). The rest of the project is MIT.

namespace nv_color_profiles.core.display;

/// <summary>
/// Gamma LUT (256 entries/channel) computed from brightness/contrast/gamma sliders.
/// Curve formula matches NVCP (algorithm: falahati/WindowsDisplayAPI, LGPL-3.0, see file header).
/// All channels share one curve; per-channel control is out of scope.
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

    /// <summary>Number of ramp entries per channel in the NVAPI-native format.</summary>
    public const int NVAPI_DATA_POINTS = 1024;

    /// <summary>Length of the R,G,B-interleaved NVAPI ramp buffer expected by the driver.</summary>
    public const int NVAPI_RAMP_LENGTH = NVAPI_DATA_POINTS * 3;

    /// <summary>
    /// Builds a 3072-entry float ramp (R,G,B interleaved) in the shape
    /// <c>NvAPI_DISP_SetTargetGammaCorrection</c> expects, using NVCP's exact curve (nvBrightness §6).
    /// UI values map linearly to driver units (b/c 0..200, gamma 40..280; 100 = neutral).
    /// </summary>
    public static float[] to_nvapi_ramp(double brightness, double contrast, double gamma)
    {
        var buffer = new float[NVAPI_RAMP_LENGTH];
        fill_nvapi_ramp(brightness, contrast, gamma, buffer);
        return buffer;
    }

    /// <summary>
    /// Same as <see cref="to_nvapi_ramp(double,double,double)"/> but writes into the caller's buffer
    /// (must be at least <see cref="NVAPI_RAMP_LENGTH"/> floats). Lets hot paths use a pooled buffer.
    /// </summary>
    public static void fill_nvapi_ramp(double brightness, double contrast, double gamma, Span<float> buffer)
    {
        if (buffer.Length < NVAPI_RAMP_LENGTH)
        {
            throw new ArgumentException(
                $"buffer must be at least {NVAPI_RAMP_LENGTH} floats, got {buffer.Length}",
                nameof(buffer));
        }

        var brightness_raw = Math.Clamp(brightness, 0, 1) * 200;
        var contrast_raw = Math.Clamp(contrast, 0, 1) * 200;
        var gamma_raw = Math.Clamp(gamma, 0.4, 2.8) * 100;

        var contrast_norm = (contrast_raw - 100) / 100.0;
        var brightness_shift = (brightness_raw - 100) / 100.0;
        var gamma_inv = 1.0 / (gamma_raw / 100.0);

        for (var i = 0; i < NVAPI_DATA_POINTS; i++)
        {
            var x = i / (double)(NVAPI_DATA_POINTS - 1);
            double val;
            if (contrast_norm <= 0)
            {
                val = (contrast_norm + 1) * (x - 0.5);
            }
            else
            {
                // guard against divide-by-zero as contrast_norm approaches 1
                val = (x - 0.5) / Math.Max(1 - contrast_norm, 1e-6);
            }
            val += brightness_shift + 0.5;
            val = Math.Clamp(val, 0, 1);
            val = Math.Pow(val, gamma_inv);
            val = Math.Clamp(val, 0, 1);

            var f = (float)val;
            buffer[i * 3 + 0] = f;
            buffer[i * 3 + 1] = f;
            buffer[i * 3 + 2] = f;
        }
    }

    private static ushort[] calculate_lut(double brightness, double contrast, double gamma)
    {
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
