using System.Diagnostics;

namespace nv_color_profiles.core.diagnostics;

/// <summary>
/// Enumerates running processes whose executable name matches a known color-touching tool. Any
/// hit here is a plausible source of "our gamma ramp got wiped" or "the sliders don't do what
/// they should" reports, because these tools either push their own LUT or hook the same display
/// pipeline surface we use.
///
/// The list is a curated allowlist to keep the report focused — a full running-process dump
/// would be noise (and privacy-hostile). Names are matched case-insensitively without the
/// <c>.exe</c> suffix, so <c>f.lux.exe</c> matches <c>flux</c>.
/// </summary>
public static class color_tool_probe
{
    // Format: (process name without .exe, human-readable label describing what it does).
    // Keep this alphabetical so additions stay easy to reason about.
    private static readonly (string exe, string label)[] KNOWN_TOOLS =
    [
        ("clickmonitorddc", "ClickMonitorDDC (DDC/CI brightness/contrast)"),
        ("controlmymonitor", "ControlMyMonitor (DDC/CI monitor control)"),
        ("displaycal-3dlut-maker", "DisplayCAL (ICC calibration)"),
        ("displaycal", "DisplayCAL (ICC calibration)"),
        ("displayfusion", "DisplayFusion (multi-monitor manager with color features)"),
        ("dwmcolorapp", "DwmColorApp"),
        ("f.lux", "f.lux (blue-light shifter, applies own gamma ramp)"),
        ("flux", "f.lux (blue-light shifter, applies own gamma ramp)"),
        ("iris", "Iris (blue-light shifter)"),
        ("lightbulb", "LightBulb (blue-light shifter, applies own gamma ramp)"),
        ("lunar", "Lunar (DDC/CI adaptive brightness)"),
        ("monitorian", "Monitorian (DDC/CI brightness)"),
        ("novideo_srgb", "novideo_srgb (NVIDIA color-clamp tool, uses same LUT surface as us)"),
        ("nvidia app", "NVIDIA App"),
        ("nvidia broadcast", "NVIDIA Broadcast"),
        ("nvcontainer", "NVIDIA Container (driver runtime)"),
        ("nvcplui", "NVIDIA Control Panel UI"),
        ("nvdisplay.container", "NVIDIA Display Container"),
        ("redshift", "Redshift (blue-light shifter, applies own gamma ramp)"),
        ("sunsetscreen", "SunsetScreen (blue-light shifter)"),
        ("twinkletray", "Twinkle Tray (DDC/CI brightness)"),
    ];

    public sealed record entry(string process_name, string label);

    /// <summary>
    /// Returns every known color-touching process currently running. Uses
    /// <see cref="Process.GetProcessesByName(string)"/> per allow-list entry so we do not need to
    /// open handles for every process on the machine (that path becomes the dominant cost when
    /// the user runs many processes — several seconds is not unusual). Never throws — every
    /// per-tool query is guarded and returns nothing on failure.
    /// </summary>
    public static IReadOnlyList<entry> probe()
    {
        var hits = new List<entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (exe, label) in KNOWN_TOOLS)
        {
            if (!seen.Add(exe))
            {
                continue;
            }
            Process[]? procs = null;
            try
            {
                procs = Process.GetProcessesByName(exe);
                if (procs.Length > 0)
                {
                    hits.Add(new entry(exe, label));
                }
            }
            catch
            {
                // per-tool failures must not sink the whole probe
            }
            finally
            {
                if (procs is not null)
                {
                    foreach (var p in procs)
                    {
                        try { p.Dispose(); } catch { /* ignore */ }
                    }
                }
            }
        }
        return hits;
    }
}
