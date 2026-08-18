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
/// Builds a support .zip a user can attach to a bug report. Bundles the config, a short system
/// summary, GPU/display info, the full log (size-capped) and the NVTweak registry subtree, with
/// aggressive PII redaction on every text entry.
/// </summary>
public static class diagnostic_bundle
{
    private const long LOG_MAX_BYTES = 5L * 1024 * 1024;

    private const string NVTWEAK_DEVICES_KEY = @"Software\NVIDIA Corporation\Global\NVTweak\Devices";

    private const string README_TEXT =
        "Attach this file when opening a bug report on GitHub. Contains: your config, system"
        + " and GPU info, the app log and driver-persistent color state.\r\n"
        + "Bundle contents are PII-redacted (usernames, user-profile paths, ICC-profile paths)."
        + " Typical bundle size is under 100 KB; if a large log is included it may be a few MB.\r\n"
        + "No screenshots. You can review the .zip contents before uploading.\r\n";

    /// <summary>A single display line for the GPU report.</summary>
    public sealed record display_entry(uint display_id, uint? luid, string gdi_name);

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
        gpu_snapshot? gpu);

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

        using (var stream = new FileStream(zip_path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            write_text(archive, "README.txt", README_TEXT);
            write_text(archive, "system.txt", redact_pii(build_system_report(bundle_inputs.app_version)));
            write_text(archive, "gpu.txt", redact_pii(build_gpu_report(bundle_inputs.gpu)));
            write_text(archive, "logs.txt", redact_pii(read_log_full(bundle_inputs.log_file_path, LOG_MAX_BYTES)));
            write_text(archive, "registry-nvtweak.txt", redact_pii(build_registry_report()));
            copy_config(archive, bundle_inputs.config_file_path);
        }

        var size = new FileInfo(zip_path).Length;
        return new export_result(zip_path, size);
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
        return sb.ToString();
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
        sb.Append("Displays:").Append("\r\n");
        if (gpu.displays.Count == 0)
        {
            sb.Append("  (none reported)\r\n");
        }
        else
        {
            foreach (var d in gpu.displays)
            {
                var luid = d.luid is { } value ? $"0x{value:X8}" : "(unresolved)";
                sb.Append("  displayId=0x").Append(d.display_id.ToString("X8"))
                  .Append(" luid=").Append(luid)
                  .Append(" gdi=").Append(string.IsNullOrEmpty(d.gdi_name) ? "(unknown)" : d.gdi_name)
                  .Append("\r\n");
            }
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

        if (!string.IsNullOrEmpty(user_name) && user_name.Length >= 3)
        {
            result = Regex.Replace(result, Regex.Escape(user_name), "<USER>", RegexOptions.IgnoreCase);
        }

        if (!string.IsNullOrEmpty(host_name) && host_name.Length >= 3)
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
