using System.Runtime.InteropServices;

namespace nv_color_profiles.core.interop.nvapi;

/// <summary>
/// Thin wrappers around GPU/display enumeration and LUID lookup NvAPI calls.
/// </summary>
public static class nv_display_enum
{
    private const int NVAPI_MAX_PHYSICAL_GPUS = 64;
    private const uint NVAPI_MAX_DISPLAYS = 256;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int enum_physical_gpus_fn(nint* handles, out int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int get_connected_display_ids_fn(
        nint gpu_handle, nv_gpu_display_id_v2* display_ids, ref uint count, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int get_luid_from_display_id_fn(uint display_id, uint flags, Guid* out_guid);

    // NV_GPU_DISPLAYIDS_V2: 4 fields, 16 bytes. The older-header reserved field is folded into
    // flags; adding it here breaks the size check.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct nv_gpu_display_id_v2
    {
        public uint version;
        public uint connector_type;
        public uint display_id;
        public uint flags;
    }

    // MAKE_NVAPI_VERSION(NV_GPU_DISPLAYIDS_V2, 3).
    public static unsafe uint make_display_id_version()
        => (uint)(sizeof(nv_gpu_display_id_v2) | (3 << 16));

    /// <summary>Returns the handles of every physical NVIDIA GPU present.</summary>
    public static unsafe nint[] enum_physical_gpus(nv_api_loader loader)
    {
        var fn = loader.resolve<enum_physical_gpus_fn>(nv_api_loader.HASH_ENUM_PHYSICAL_GPUS);
        var handles = stackalloc nint[NVAPI_MAX_PHYSICAL_GPUS];
        var status = fn(handles, out var count);
        if (status != 0 || count <= 0)
        {
            return Array.Empty<nint>();
        }
        var result = new nint[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = handles[i];
        }
        return result;
    }

    /// <summary>
    /// Returns the connected display IDs for a given GPU handle. On NvAPI failure returns an empty
    /// array; the raw status is not surfaced.
    /// </summary>
    public static unsafe uint[] get_connected_display_ids(nv_api_loader loader, nint gpu_handle)
    {
        var fn = loader.resolve<get_connected_display_ids_fn>(nv_api_loader.HASH_GPU_GET_CONNECTED_DISPLAY_IDS);
        var version = make_display_id_version();

        var buffer = new nv_gpu_display_id_v2[NVAPI_MAX_DISPLAYS];
        for (var i = 0; i < NVAPI_MAX_DISPLAYS; i++)
        {
            buffer[i].version = version;
        }
        var count = NVAPI_MAX_DISPLAYS;

        int status;
        fixed (nv_gpu_display_id_v2* pin = buffer)
        {
            status = fn(gpu_handle, pin, ref count, 0);
        }
        if (status != 0 || count == 0)
        {
            return Array.Empty<uint>();
        }

        var ids = new uint[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = buffer[i].display_id;
        }
        return ids;
    }

    /// <summary>
    /// Returns the 32-bit LUID used as the NVTweak registry sub-key, or null on failure.
    /// Recipe: dword[1] of the driver GUID XORed with <c>0xF0000000</c> (as nvBrightness does).
    /// </summary>
    public static unsafe uint? get_luid_for_display(nv_api_loader loader, uint display_id)
    {
        var fn = loader.resolve<get_luid_from_display_id_fn>(nv_api_loader.HASH_SYS_GET_LUID_FROM_DISPLAY_ID);
        Guid guid;
        var status = fn(display_id, 1, &guid);
        if (status != 0)
        {
            return null;
        }
        Span<byte> bytes = stackalloc byte[16];
        var written = guid.TryWriteBytes(bytes);
        if (!written)
        {
            return null;
        }
        var dword_index_1 = BitConverter.ToUInt32(bytes[4..8]);
        return dword_index_1 ^ 0xF0000000u;
    }
}
