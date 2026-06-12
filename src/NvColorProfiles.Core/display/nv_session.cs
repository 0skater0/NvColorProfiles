using Microsoft.Extensions.Logging;
using NvAPIWrapper;

namespace nv_color_profiles.core.display;

/// <summary>
/// Owns the NvAPI lifetime: <c>Initialize()</c> on construction, <c>Unload()</c> on dispose.
/// <see cref="is_available"/> is false when there is no NVIDIA GPU/driver, letting callers
/// degrade gracefully instead of crashing.
/// </summary>
public sealed class nv_session : IDisposable
{
    private readonly ILogger<nv_session> log;
    private bool initialized;

    public nv_session(ILogger<nv_session> log)
    {
        this.log = log;
        try
        {
            NVIDIA.Initialize();
            initialized = true;
            log.LogInformation(
                "NvAPI initialized (driver {driver}, branch {branch})",
                format_driver(NVIDIA.DriverVersion),
                NVIDIA.DriverBranchVersion);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "NvAPI unavailable (no NVIDIA GPU or driver?) — color control disabled");
        }
    }

    public bool is_available => initialized;

    public void Dispose()
    {
        if (!initialized)
        {
            return;
        }

        try
        {
            NVIDIA.Unload();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "NvAPI unload failed");
        }
        initialized = false;
    }

    /// <summary>NvAPI reports the driver as an integer like 61047 → "610.47".</summary>
    private static string format_driver(uint version) => $"{version / 100}.{version % 100:D2}";
}
