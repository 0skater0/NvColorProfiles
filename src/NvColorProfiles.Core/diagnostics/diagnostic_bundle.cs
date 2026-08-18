using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace nv_color_profiles.core.diagnostics;

/// <summary>
/// Builds a support .zip a user can attach to a bug report. Bundles a triage summary, config,
/// a system+environment report, per-display GPU and color state, a plain-language NVCP snapshot,
/// a probe of known third-party color tools, the full log (size-capped), and the raw NVTweak
/// registry subtree as a backup. Every text entry runs through PII redaction.
/// </summary>
public static class diagnostic_bundle
{
    private const long LOG_MAX_BYTES = 5L * 1024 * 1024;

    private const string NVTWEAK_DEVICES_KEY = @"Software\NVIDIA Corporation\Global\NVTweak\Devices";

    private const string README_TEXT =
        "Attach this file when opening a bug report on GitHub. Contains: a short triage summary,"
        + " your config, system and GPU/display state (including HDR, ICC profile, Night Light and"
        + " Windows color filter), NVIDIA Control Panel color state per device, a probe of known"
        + " third-party color tools, the app log, and the raw NVTweak registry branch.\r\n"
        + "Bundle contents are PII-redacted (usernames, user-profile paths, ICC-profile paths)."
        + " Typical bundle size is under 200 KB; if a large log is included it may be a few MB.\r\n"
        + "No screenshots. You can review the .zip contents before uploading.\r\n";

    /// <summary>A single display line for the GPU report.</summary>
    public sealed record display_entry(uint display_id, uint? luid, string gdi_name)
    {
        /// <summary>Monitor model name as reported by the EDID (e.g. "LG UltraGear 34GN850").</summary>
        public string? friendly_name { get; init; }

        /// <summary>Three-letter EDID manufacturer code (e.g. "LGD", "SAM").</summary>
        public string? edid_vendor { get; init; }

        /// <summary>EDID product code, opaque per-panel-model identifier.</summary>
        public ushort? edid_product_code { get; init; }

        /// <summary>Current NVIDIA Digital Vibrance percentage (50 = neutral), null if not queryable.</summary>
        public int? current_vibrance { get; init; }

        /// <summary>Current NVIDIA Hue angle in degrees (0 = neutral), null if not queryable.</summary>
        public int? current_hue { get; init; }

        /// <summary>HDR / Advanced Color state, null when the DisplayConfig query failed.</summary>
        public hdr_snapshot? hdr { get; init; }

        /// <summary>Filename of the currently assigned ICC profile, or null if none / unresolved.</summary>
        public string? icc_profile { get; init; }
    }

    /// <summary>GPU/driver snapshot; null when no NVAPI session is available.</summary>
    public sealed record gpu_snapshot(
        string driver_version,
        string driver_branch,
        IReadOnlyList<string> gpu_names,
        IReadOnlyList<display_entry> displays);

    /// <summary>Inputs the caller assembles; keeps this class independent of UI/host services.</summary>
    public sealed record inputs(
        string app_version,
        string config_file_path,
        string log_file_path,
        gpu_snapshot? gpu)
    {
        /// <summary>The persisted app mode ("manual" / "automatic" / ...) for the summary line.</summary>
        public string? app_mode { get; init; }

        /// <summary>Name of the profile the app remembers as active.</summary>
        public string? active_profile { get; init; }

        /// <summary>Profile count, rule count, schedule count — one-line inventory for triage.</summary>
        public int profile_count { get; init; }

        /// <summary>Rule count for the summary line.</summary>
        public int rule_count { get; init; }

        /// <summary>Schedule count for the summary line.</summary>
        public int schedule_count { get; init; }

        /// <summary>True when the NVAPI gamma backend initialised successfully at startup.</summary>
        public bool gamma_available { get; init; }
    }

    /// <summary>Result of a bundle export: the resulting path and its size in bytes.</summary>
    public sealed record export_result(string zip_path, long zip_bytes);

    // Matches any Windows user-profile prefix (drive letter + Users + one path segment).
    private static readonly Regex user_profile_path_regex = new(
        @"[Cc]:[\\/]+[Uu]sers[\\/]+[^\\/\s""'<>|:*?]+",
        RegexOptions.Compiled);

    // Matches a full registry line whose value name is NvCplICCProfile; captures prefix + path.
    // No $ anchor: the char class already stops at line breaks and $ under Multiline does not
    // match before \r on CRLF streams.
    private static readonly Regex icc_line_regex = new(
        @"^(?<prefix>[^\r\n]*NvCplICCProfile[^\r\n]*=\s*)(?<path>[^\r\n]+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Recognises an "Applied profile 'X' to N display(s)" line in the log, latest wins.
    // The name capture is non-greedy up to the trailing " to N display" suffix so profile names
    // that contain their own apostrophes (e.g. "Lukas' Setup") are preserved intact.
    private static readonly Regex applied_line_regex = new(
        @"^(?<ts>\S+\s+\S+)\s+\[INF\]\s+profile_service\s+-\s+Applied profile '(?<name>.+?)' to \d+ display",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Returns "NvColorProfiles-diagnostic-YYYYMMDD-HHmmss.zip".</summary>
    public static string default_file_name(DateTime timestamp)
        => $"NvColorProfiles-diagnostic-{timestamp:yyyyMMdd-HHmmss}.zip";

    /// <summary>
    /// Default target folder. Portable mode writes next to the exe so a portable install stays
    /// self-contained; roaming mode writes to <c>%USERPROFILE%\Downloads</c> (falls back to temp).
    /// </summary>
    public static string default_output_dir()
    {
        if (app_paths.is_portable)
        {
            return app_paths.executable_dir;
        }
        var user_profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(user_profile))
        {
            var downloads = Path.Combine(user_profile, "Downloads");
            if (Directory.Exists(downloads))
            {
                return downloads;
            }
        }
        return Path.GetTempPath();
    }

    /// <summary>Writes the bundle to <paramref name="output_dir"/> and returns path + byte size.</summary>
    public static export_result export(inputs bundle_inputs, string output_dir)
    {
        Directory.CreateDirectory(output_dir);
        var zip_path = Path.Combine(output_dir, default_file_name(DateTime.Now));
        return export_to_file(bundle_inputs, zip_path);
    }

    /// <summary>Writes the bundle to the explicit <paramref name="zip_path"/> (creates parent dirs).</summary>
    public static export_result export_to_file(inputs bundle_inputs, string zip_path)
    {
        var parent = Path.GetDirectoryName(zip_path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var log_text = read_log_full(bundle_inputs.log_file_path, LOG_MAX_BYTES);

        using (var stream = new FileStream(zip_path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            write_text(archive, "summary.txt", redact_pii(build_summary_report(bundle_inputs, log_text)));
            write_text(archive, "README.txt", README_TEXT);
            write_text(archive, "system.txt", redact_pii(build_system_report(bundle_inputs.app_version)));
            write_text(archive, "gpu.txt", redact_pii(build_gpu_report(bundle_inputs.gpu)));
            write_text(archive, "nvcp-state.txt", redact_pii(build_nvcp_report()));
            write_text(archive, "color-tools.txt", build_color_tools_report());
            write_text(archive, "logs.txt", redact_pii(log_text));
            write_text(archive, "registry-nvtweak.txt", redact_pii(build_registry_report()));
            copy_config(archive, bundle_inputs.config_file_path);
        }

        var size = new FileInfo(zip_path).Length;
        return new export_result(zip_path, size);
    }

    /// <summary>
    /// Public for tests: builds the triage summary. Uses the log text passed in so we do not
    /// re-read the log twice during an export.
    /// </summary>
    public static string build_summary_report(inputs bundle_inputs, string log_text)
    {
        var sb = new StringBuilder();
        sb.Append("Triage summary — read this first.\r\n\r\n");
        sb.Append("App version:      ").Append(bundle_inputs.app_version).Append("\r\n");
        sb.Append("Switching mode:   ").Append(bundle_inputs.app_mode ?? "(unknown)").Append("\r\n");
        sb.Append("Active profile:   ").Append(bundle_inputs.active_profile ?? "(unknown)").Append("\r\n");
        sb.Append("Profiles/rules/schedules: ")
            .Append(bundle_inputs.profile_count).Append(" / ")
            .Append(bundle_inputs.rule_count).Append(" / ")
            .Append(bundle_inputs.schedule_count).Append("\r\n");
        sb.Append("Gamma backend:    ").Append(bundle_inputs.gamma_available ? "available" : "unavailable").Append("\r\n");

        var (last_ts, last_name) = last_applied_from_log(log_text);
        sb.Append("Last profile actually applied (from log): ");
        if (last_name is null)
        {
            sb.Append("(none in current log)\r\n");
        }
        else
        {
            sb.Append('\'').Append(last_name).Append("'  @ ").Append(last_ts).Append("\r\n");
        }

        if (!string.Equals(last_name, bundle_inputs.active_profile, StringComparison.OrdinalIgnoreCase)
            && last_name is not null
            && bundle_inputs.active_profile is not null)
        {
            sb.Append("Note: the log's last apply differs from the active profile — the app may be in manual mode with a stale selection, or a profile switch is queued.\r\n");
        }
        return sb.ToString();
    }

    private static (string ts, string? name) last_applied_from_log(string log_text)
    {
        if (string.IsNullOrEmpty(log_text))
        {
            return ("", null);
        }
        Match? last = null;
        foreach (Match m in applied_line_regex.Matches(log_text))
        {
            last = m;
        }
        if (last is null)
        {
            return ("", null);
        }
        return (last.Groups["ts"].Value, last.Groups["name"].Value);
    }

    /// <summary>Public for tests: format a system summary without side effects.</summary>
    public static string build_system_report(string app_version)
    {
        var sb = new StringBuilder();
        sb.Append("App version: ").Append(app_version).Append("\r\n");
        sb.Append("Machine name: ").Append(Environment.MachineName).Append("\r\n");
        sb.Append("OS: ").Append(Environment.OSVersion.VersionString).Append("\r\n");
        sb.Append("OS architecture: ").Append(RuntimeInformation.OSArchitecture).Append("\r\n");
        sb.Append("Process architecture: ").Append(RuntimeInformation.ProcessArchitecture).Append("\r\n");
        sb.Append(".NET runtime: ").Append(RuntimeInformation.FrameworkDescription).Append("\r\n");
        sb.Append("Runtime identifier: ").Append(RuntimeInformation.RuntimeIdentifier).Append("\r\n");
        sb.Append("UI culture: ").Append(CultureInfo.CurrentUICulture.Name).Append("\r\n");
        sb.Append("Culture: ").Append(CultureInfo.CurrentCulture.Name).Append("\r\n");

        if (OperatingSystem.IsWindows())
        {
            append_windows_system_details(sb);
        }
        return sb.ToString();
    }

    [SupportedOSPlatform("windows")]
    private static void append_windows_system_details(StringBuilder sb)
    {
        var win = windows_version_info.query();
        sb.Append("Windows: ").Append(windows_version_info.format(win)).Append("\r\n");

        var nl = night_light_info.query();
        sb.Append("Windows Night Light: ")
            .Append(!nl.present ? "never configured" : nl.enabled ? "ENABLED" : "off")
            .Append("\r\n");

        var cf = color_filter_info.query();
        sb.Append("Windows Color Filter: ");
        if (!cf.active)
        {
            sb.Append("off");
        }
        else
        {
            sb.Append("ACTIVE (").Append(cf.type).Append(")");
        }
        sb.Append("\r\n");
    }

    /// <summary>Public for tests: format the GPU report from a snapshot (or a stub when absent).</summary>
    public static string build_gpu_report(gpu_snapshot? gpu)
    {
        if (gpu is null)
        {
            return "NVAPI not available (no NVIDIA GPU or driver, or initialization failed).\r\n";
        }
        var sb = new StringBuilder();
        sb.Append("Driver version: ").Append(gpu.driver_version).Append("\r\n");
        sb.Append("Driver branch: ").Append(gpu.driver_branch).Append("\r\n");
        sb.Append("GPUs:").Append("\r\n");
        if (gpu.gpu_names.Count == 0)
        {
            sb.Append("  (none reported)\r\n");
        }
        else
        {
            for (var i = 0; i < gpu.gpu_names.Count; i++)
            {
                sb.Append("  ").Append(i).Append(": ").Append(gpu.gpu_names[i]).Append("\r\n");
            }
        }
        sb.Append("\r\nDisplays:").Append("\r\n");
        if (gpu.displays.Count == 0)
        {
            sb.Append("  (none reported)\r\n");
        }
        else
        {
            foreach (var d in gpu.displays)
            {
                var luid = d.luid is { } value ? $"0x{value:X8}" : "(unresolved)";
                var label = !string.IsNullOrEmpty(d.friendly_name)
                    ? d.friendly_name!
                    : (string.IsNullOrEmpty(d.gdi_name) ? "(unknown)" : d.gdi_name);
                sb.Append("  ").Append(label).Append("\r\n");
                sb.Append("    gdi: ").Append(string.IsNullOrEmpty(d.gdi_name) ? "(unknown)" : d.gdi_name).Append("\r\n");
                sb.Append("    displayId=0x").Append(d.display_id.ToString("X8"))
                  .Append(" luid=").Append(luid).Append("\r\n");
                if (!string.IsNullOrEmpty(d.edid_vendor) || d.edid_product_code is not null)
                {
                    sb.Append("    edid: vendor=").Append(d.edid_vendor ?? "?")
                      .Append(" product=0x").Append((d.edid_product_code ?? 0).ToString("X4"))
                      .Append("\r\n");
                }
                if (d.hdr is { } h)
                {
                    // Only the fields that are documented and stable across driver versions land
                    // in the report. activeColorMode (SDR/WCG/HDR) is the authoritative signal in
                    // v2; the derived toggle bits are parsed into the snapshot record but not
                    // surfaced here until their exact layout is confirmed against the shipping
                    // SDK header. On v1 systems the boolean "advanced color" flag is well-known.
                    sb.Append("    color pipeline: active mode=").Append(h.active)
                      .Append(", bpc=").Append(h.bits_per_channel);
                    if (!h.from_v2)
                    {
                        sb.Append(", advanced color=").Append(h.enabled ? "on" : "off");
                    }
                    sb.Append("\r\n");
                }
                sb.Append("    ICC profile: ")
                  .Append(string.IsNullOrEmpty(d.icc_profile) ? "system default (sRGB)" : d.icc_profile)
                  .Append("\r\n");
                if (d.current_vibrance is not null || d.current_hue is not null)
                {
                    sb.Append("    current vibrance/hue: ")
                      .Append(d.current_vibrance?.ToString(CultureInfo.InvariantCulture) ?? "?")
                      .Append(" / ")
                      .Append(d.current_hue?.ToString(CultureInfo.InvariantCulture) ?? "?")
                      .Append("\r\n");
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Public for tests: aggregated NVIDIA Control Panel color state per device, in plain text.
    /// Falls back to a stub on non-Windows platforms.
    /// </summary>
    public static string build_nvcp_report()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "(NVIDIA Control Panel state only available on Windows)\r\n";
        }
        return build_nvcp_report_windows();
    }

    [SupportedOSPlatform("windows")]
    private static string build_nvcp_report_windows()
    {
        var sb = new StringBuilder();
        sb.Append("NVIDIA Control Panel color state per device.\r\n");
        sb.Append("Note: NVCP device ids do not map 1:1 to NvAPI display ids. Entries here cover\r\n");
        sb.Append("every panel NVCP has ever seen, including disconnected ones. Cross-check the\r\n");
        sb.Append("device count against your connected monitors in gpu.txt.\r\n\r\n");

        var devices = nvcp_state.enumerate();
        if (devices.Count == 0)
        {
            sb.Append("(no populated NVCP color device entries found)\r\n");
            return sb.ToString();
        }

        var any_enabled = false;
        foreach (var d in devices)
        {
            sb.Append("Device ").Append(d.nvcp_device_id).Append(":\r\n");
            sb.Append("  color correction enabled: ").Append(d.color_correction_enabled ? "YES" : "no").Append("\r\n");
            if (d.color_correction_enabled)
            {
                any_enabled = true;
            }
            sb.Append("  brightness R/G/B: ").Append(d.brightness_r).Append(" / ").Append(d.brightness_g).Append(" / ").Append(d.brightness_b).Append("\r\n");
            sb.Append("  contrast   R/G/B: ").Append(d.contrast_r).Append(" / ").Append(d.contrast_g).Append(" / ").Append(d.contrast_b).Append("\r\n");
            sb.Append("  gamma      R/G/B: ").Append(d.gamma_r).Append(" / ").Append(d.gamma_g).Append(" / ").Append(d.gamma_b).Append("\r\n");
            sb.Append("  vibrance   R/G/B: ").Append(d.vibrance_r).Append(" / ").Append(d.vibrance_g).Append(" / ").Append(d.vibrance_b).Append("\r\n");
            sb.Append("  hue        R/G/B: ").Append(d.hue_r).Append(" / ").Append(d.hue_g).Append(" / ").Append(d.hue_b).Append("\r\n");
            sb.Append("  external color ramp: ");
            if (!d.has_external_colors)
            {
                sb.Append("(none)");
            }
            else if (d.external_colors_is_identity)
            {
                sb.Append("identity (no visible effect)");
            }
            else
            {
                sb.Append("custom LUT stored");
            }
            sb.Append("\r\n");
            if (!string.IsNullOrEmpty(d.icc_profile_filename))
            {
                sb.Append("  ICC profile assigned by NVCP: ").Append(d.icc_profile_filename).Append("\r\n");
            }
            if (d.at_default)
            {
                sb.Append("  → all sliders at driver default (100)\r\n");
            }
            sb.Append("\r\n");
        }
        if (any_enabled)
        {
            sb.Append("HINT: at least one device has NVCP color correction ENABLED. NVCP writes into\r\n");
            sb.Append("the same driver pipeline stage NvColorProfiles uses — if the two disagree,\r\n");
            sb.Append("the last writer wins and results can look inconsistent. Reset NVCP color\r\n");
            sb.Append("settings to default (\"Restore Defaults\" in \"Adjust desktop color settings\")\r\n");
            sb.Append("before comparing outputs.\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Public for tests: list of currently running known color-touching tools.</summary>
    public static string build_color_tools_report()
    {
        var sb = new StringBuilder();
        sb.Append("Known color-related third-party tools currently running.\r\n");
        sb.Append("Any hit here can push its own gamma LUT or hook the display pipeline the app uses.\r\n\r\n");
        var hits = color_tool_probe.probe();
        if (hits.Count == 0)
        {
            sb.Append("(none detected)\r\n");
            return sb.ToString();
        }
        foreach (var h in hits)
        {
            sb.Append("- ").Append(h.process_name).Append(" — ").Append(h.label).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reads the full log file. If it exceeds <paramref name="max_bytes"/>, keeps only the last
    /// <paramref name="max_bytes"/> aligned to the next line boundary and prepends a truncation marker.
    /// </summary>
    public static string read_log_full(string log_file_path, long max_bytes)
    {
        if (!File.Exists(log_file_path))
        {
            return "(no log file present)\r\n";
        }
        try
        {
            using var stream = new FileStream(log_file_path, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete);
            var total_bytes = stream.Length;
            if (total_bytes <= max_bytes)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }

            var start_offset = total_bytes - max_bytes;
            long dropped_lines = count_lines_and_advance(stream, start_offset);

            // Align to the next line boundary so the first shown line is complete.
            int b;
            while ((b = stream.ReadByte()) != -1 && b != '\n')
            {
                // discard partial line
            }
            if (b == '\n')
            {
                dropped_lines++;
            }

            using var tail_reader = new StreamReader(stream);
            var tail = tail_reader.ReadToEnd();
            var marker = string.Format(
                CultureInfo.InvariantCulture,
                "[truncated: showing last {0} of {1} total, {2} lines dropped]\r\n",
                format_bytes(max_bytes), format_bytes(total_bytes), dropped_lines);
            return marker + tail;
        }
        catch (Exception ex)
        {
            return $"(failed to read log: {ex.Message})\r\n";
        }
    }

    /// <summary>Redacts common Windows PII from a text blob: usernames, user-profile paths,
    /// user-owned ICC profile paths, and the machine name. Safe to call on empty input.</summary>
    public static string redact_pii(string content)
        => redact_pii_core(content, Environment.UserName, Environment.MachineName);

    /// <summary>Internal test seam: same as <see cref="redact_pii"/> but with injectable identity.</summary>
    internal static string redact_pii_core(string content, string user_name, string host_name)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        var result = user_profile_path_regex.Replace(content, @"C:\Users\<USER>");

        // Minimum length 4 avoids collisions with three-letter tokens that legitimately show up
        // in the bundle (EDID vendor codes like "GSM", "LGD", "SAM"; driver branch identifiers;
        // hex byte pairs). Windows account names shorter than that are rare in practice.
        if (!string.IsNullOrEmpty(user_name) && user_name.Length >= 4)
        {
            result = Regex.Replace(result, Regex.Escape(user_name), "<USER>", RegexOptions.IgnoreCase);
        }

        if (!string.IsNullOrEmpty(host_name) && host_name.Length >= 4)
        {
            result = Regex.Replace(result, Regex.Escape(host_name), "<HOSTNAME>", RegexOptions.IgnoreCase);
        }

        result = redact_user_icc_paths(result);
        return result;
    }

    private static string redact_user_icc_paths(string content)
    {
        return icc_line_regex.Replace(content, m =>
        {
            var path = m.Groups["path"].Value;
            if (!path.Contains("<USER>", StringComparison.Ordinal))
            {
                return m.Value;
            }
            var last_sep = path.LastIndexOfAny(new[] { '\\', '/' });
            if (last_sep < 0)
            {
                return m.Value;
            }
            var filename = path[(last_sep + 1)..];
            return m.Groups["prefix"].Value + @"<REDACTED>\" + filename;
        });
    }

    private static long count_lines_and_advance(Stream stream, long byte_count)
    {
        long lines = 0;
        var buffer = new byte[64 * 1024];
        long read_so_far = 0;
        while (read_so_far < byte_count)
        {
            var to_read = (int)Math.Min(buffer.Length, byte_count - read_so_far);
            var n = stream.Read(buffer, 0, to_read);
            if (n <= 0)
            {
                break;
            }
            for (var i = 0; i < n; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    lines++;
                }
            }
            read_so_far += n;
        }
        return lines;
    }

    private static string format_bytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F1} MB", bytes / (1024.0 * 1024.0));
        }
        if (bytes >= 1024)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F1} KB", bytes / 1024.0);
        }
        return $"{bytes} B";
    }

    /// <summary>Dumps the NVTweak\Devices HKCU subtree; safe no-op on non-Windows.</summary>
    public static string build_registry_report()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "(registry dump only available on Windows)\r\n";
        }
        return dump_nvtweak_windows();
    }

    [SupportedOSPlatform("windows")]
    private static string dump_nvtweak_windows()
    {
        var sb = new StringBuilder();
        sb.Append(@"HKCU\").Append(NVTWEAK_DEVICES_KEY).Append("\r\n");
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(NVTWEAK_DEVICES_KEY);
            if (root is null)
            {
                sb.Append("  (key not present)\r\n");
                return sb.ToString();
            }
            append_key_values(sb, root, indent: "  ");
            foreach (var sub_name in root.GetSubKeyNames())
            {
                sb.Append("  [").Append(sub_name).Append("]\r\n");
                using var sub = root.OpenSubKey(sub_name);
                if (sub is null)
                {
                    continue;
                }
                append_key_values(sb, sub, indent: "    ");
                foreach (var color_sub_name in sub.GetSubKeyNames())
                {
                    sb.Append("    [").Append(color_sub_name).Append("]\r\n");
                    using var color_sub = sub.OpenSubKey(color_sub_name);
                    if (color_sub is null)
                    {
                        continue;
                    }
                    append_key_values(sb, color_sub, indent: "      ");
                }
            }
        }
        catch (Exception ex)
        {
            sb.Append("  (failed to read: ").Append(ex.Message).Append(")\r\n");
        }
        return sb.ToString();
    }

    [SupportedOSPlatform("windows")]
    private static void append_key_values(StringBuilder sb, RegistryKey key, string indent)
    {
        foreach (var value_name in key.GetValueNames())
        {
            var kind = key.GetValueKind(value_name);
            var raw = key.GetValue(value_name);
            sb.Append(indent).Append(value_name.Length == 0 ? "(default)" : value_name)
              .Append(" [").Append(kind).Append("] = ")
              .Append(format_value(raw, kind)).Append("\r\n");
        }
    }

    [SupportedOSPlatform("windows")]
    private static string format_value(object? raw, RegistryValueKind kind)
    {
        if (raw is null)
        {
            return "(null)";
        }
        return kind switch
        {
            RegistryValueKind.MultiString => "[ " + string.Join(", ", (string[])raw) + " ]",
            RegistryValueKind.Binary => Convert.ToHexString((byte[])raw),
            _ => raw.ToString() ?? "(null)",
        };
    }

    private static void copy_config(ZipArchive archive, string config_file_path)
    {
        if (!File.Exists(config_file_path))
        {
            write_text(archive, "config.json", "{}\r\n");
            return;
        }
        try
        {
            var raw = File.ReadAllText(config_file_path);
            write_text(archive, "config.json", redact_pii(raw));
        }
        catch (Exception ex)
        {
            write_text(archive, "config.json", $"(failed to read config: {ex.Message})\r\n");
        }
    }

    private static void write_text(ZipArchive archive, string entry_name, string content)
    {
        var entry = archive.CreateEntry(entry_name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    /// <summary>Convenience: reads the running executable's informational version, then file version.</summary>
    public static string discover_app_version()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
        var name_version = assembly.GetName().Version;
        return name_version?.ToString(3) ?? "unknown";
    }
}
