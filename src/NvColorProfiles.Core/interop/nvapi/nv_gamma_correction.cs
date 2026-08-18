using System.Runtime.InteropServices;

namespace nv_color_profiles.core.interop.nvapi;

/// <summary>
/// Undocumented <c>NvAPI_DISP_SetTargetGammaCorrection</c> (NVCP's "Desktop Color Settings").
/// Writes the display-pipeline LUT downstream of game rendering, so it survives fullscreen-exclusive.
/// </summary>
public static class nv_gamma_correction
{
    public const int RAMP_LENGTH = 1024;
    public const int RAMP_CHANNELS = 3;

    /// <summary>
    /// Mirrors nvBrightness's <c>NV_GAMMA_CORRECTION_EX</c>: R,G,B interleaved per ramp index.
    /// <c>unknown</c> must be 1; the call rejects other values.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct nv_gamma_correction_ex
    {
        public uint version;
        public fixed float gamma_ramp_ex[RAMP_CHANNELS * RAMP_LENGTH];
        public uint unknown;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int set_target_gamma_correction_fn(uint display_id, nv_gamma_correction_ex* data);

    /// <summary>Builds the NVAPI version field: <c>sizeof(struct) | (version &lt;&lt; 16)</c>.</summary>
    public static unsafe uint make_version()
        => (uint)(sizeof(nv_gamma_correction_ex) | (1 << 16));

    /// <summary>
    /// Pushes a ramp to the display's driver LUT. <paramref name="channels"/> must be 3072 floats
    /// in [0,1], R,G,B interleaved. Returns the raw NvAPI status (0 = success).
    /// </summary>
    public static unsafe int apply(nv_api_loader loader, uint display_id, ReadOnlySpan<float> channels)
    {
        if (channels.Length != RAMP_CHANNELS * RAMP_LENGTH)
        {
            throw new ArgumentException(
                $"channels must be {RAMP_CHANNELS * RAMP_LENGTH} floats, got {channels.Length}",
                nameof(channels));
        }

        var data = default(nv_gamma_correction_ex);
        data.version = make_version();
        data.unknown = 1;
        for (var i = 0; i < channels.Length; i++)
        {
            data.gamma_ramp_ex[i] = channels[i];
        }

        var fn = loader.resolve<set_target_gamma_correction_fn>(nv_api_loader.HASH_DISP_SET_TARGET_GAMMA);
        return fn(display_id, &data);
    }
}
