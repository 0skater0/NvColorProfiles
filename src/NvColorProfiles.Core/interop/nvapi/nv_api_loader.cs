using System.Runtime.InteropServices;

namespace nv_color_profiles.core.interop.nvapi;

/// <summary>
/// Resolves undocumented NvAPI functions via <c>nvapi_QueryInterface</c> hash lookup, for calls
/// the upstream NvAPIWrapper.Net does not expose (notably <c>NvAPI_DISP_SetTargetGammaCorrection</c>).
/// Initialises independently of <c>nv_session</c>; NvAPI is process-wide refcounted.
/// </summary>
public sealed class nv_api_loader : IDisposable
{
    // QueryInterface hashes (stable since ~R290).
    public const uint HASH_INITIALIZE                     = 0x0150E828;
    public const uint HASH_UNLOAD                         = 0xD22BDD7E;
    public const uint HASH_ENUM_PHYSICAL_GPUS             = 0xE5AC921F;
    public const uint HASH_GPU_GET_CONNECTED_DISPLAY_IDS  = 0x0078DBA2;
    public const uint HASH_SYS_GET_LUID_FROM_DISPLAY_ID   = 0xD4A859F2;
    public const uint HASH_DISP_SET_TARGET_GAMMA          = 0x7082A053;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint nvapi_query_interface_fn(uint hash);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int nvapi_initialize_fn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int nvapi_unload_fn();

    private readonly nint module;
    private readonly nvapi_query_interface_fn query_interface;
    private readonly Dictionary<uint, nint> pointer_cache = new();
    private readonly object gate = new();
    private bool initialized;
    private bool disposed;

    /// <summary>
    /// Loads nvapi64.dll and calls <c>NvAPI_Initialize</c>. Throws when the driver is absent;
    /// callers catch to fall back to a non-NVIDIA path.
    /// </summary>
    public nv_api_loader()
    {
        module = NativeLibrary.Load("nvapi64.dll");
        var qi_ptr = NativeLibrary.GetExport(module, "nvapi_QueryInterface");
        query_interface = Marshal.GetDelegateForFunctionPointer<nvapi_query_interface_fn>(qi_ptr);

        var init_ptr = query_interface(HASH_INITIALIZE);
        if (init_ptr == 0)
        {
            throw new InvalidOperationException("NvAPI: NvAPI_Initialize not found by QueryInterface");
        }
        var init = Marshal.GetDelegateForFunctionPointer<nvapi_initialize_fn>(init_ptr);
        var status = init();
        if (status != 0)
        {
            throw new InvalidOperationException($"NvAPI_Initialize failed with status {status}");
        }
        initialized = true;
    }

    /// <summary>Returns whether Initialize succeeded (Dispose flips this back to false).</summary>
    public bool is_initialized => initialized && !disposed;

    /// <summary>Resolves an NvAPI function to a delegate; pointers are cached per hash.</summary>
    public T resolve<T>(uint hash) where T : Delegate
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(nv_api_loader));
        }

        nint ptr;
        lock (gate)
        {
            if (!pointer_cache.TryGetValue(hash, out ptr))
            {
                ptr = query_interface(hash);
                if (ptr == 0)
                {
                    throw new InvalidOperationException($"NvAPI: hash 0x{hash:X8} not found");
                }
                pointer_cache[hash] = ptr;
            }
        }
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        try
        {
            if (initialized)
            {
                var unload_ptr = query_interface(HASH_UNLOAD);
                if (unload_ptr != 0)
                {
                    var unload = Marshal.GetDelegateForFunctionPointer<nvapi_unload_fn>(unload_ptr);
                    unload();
                }
                initialized = false;
            }
        }
        catch
        {
            // dispose must not throw
        }

        if (module != 0)
        {
            NativeLibrary.Free(module);
        }
    }
}
