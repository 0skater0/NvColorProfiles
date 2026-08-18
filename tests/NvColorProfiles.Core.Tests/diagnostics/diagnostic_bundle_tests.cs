using System.IO.Compression;
using System.Text;
using nv_color_profiles.core.diagnostics;

namespace nv_color_profiles.core.tests.diagnostics;

public sealed class diagnostic_bundle_tests : IDisposable
{
    private readonly string temp_dir;

    public diagnostic_bundle_tests()
    {
        temp_dir = Path.Combine(Path.GetTempPath(), "nvcp_diag_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp_dir);
    }

    [Fact]
    public void default_file_name_uses_timestamp_pattern()
    {
        var name = diagnostic_bundle.default_file_name(new DateTime(2026, 8, 17, 11, 42, 34));
        Assert.Equal("NvColorProfiles-diagnostic-20260817-114234.zip", name);
    }

    [Fact]
    public void build_gpu_report_reports_missing_nvapi()
    {
        var text = diagnostic_bundle.build_gpu_report(null);
        Assert.Contains("NVAPI not available", text);
    }

    [Fact]
    public void build_gpu_report_lists_gpus_and_displays()
    {
        var snapshot = new diagnostic_bundle.gpu_snapshot(
            driver_version: "610.88",
            driver_branch: "r610_85",
            gpu_names: new[] { "NVIDIA RTX 5090" },
            displays: new[]
            {
                new diagnostic_bundle.display_entry(0x80000001u, 0xDEADBEEFu, "\\\\.\\DISPLAY1"),
                new diagnostic_bundle.display_entry(0x80000002u, null, "\\\\.\\DISPLAY2"),
            });

        var text = diagnostic_bundle.build_gpu_report(snapshot);

        Assert.Contains("610.88", text);
        Assert.Contains("r610_85", text);
        Assert.Contains("NVIDIA RTX 5090", text);
        Assert.Contains("displayId=0x80000001", text);
        Assert.Contains("luid=0xDEADBEEF", text);
        Assert.Contains("(unresolved)", text);
    }

    [Fact]
    public void read_log_full_returns_placeholder_when_missing()
    {
        var text = diagnostic_bundle.read_log_full(Path.Combine(temp_dir, "does-not-exist.log"), 5 * 1024 * 1024);
        Assert.Contains("no log file", text);
    }

    [Fact]
    public void read_log_full_returns_full_content_when_under_cap()
    {
        var log = Path.Combine(temp_dir, "small.log");
        var lines = Enumerable.Range(1, 500).Select(i => $"line-{i}").ToArray();
        File.WriteAllLines(log, lines);

        var text = diagnostic_bundle.read_log_full(log, 5 * 1024 * 1024);

        Assert.DoesNotContain("[truncated:", text);
        Assert.Contains("line-1\r\n", text);
        Assert.Contains("line-250\r\n", text);
        Assert.Contains("line-500", text);
    }

    [Fact]
    public void read_log_full_caps_and_marks_truncation()
    {
        var log = Path.Combine(temp_dir, "big.log");
        // ~200 bytes per line * 20000 lines = ~4 MB; cap at 1 MB triggers truncation.
        var padding = new string('x', 180);
        var lines = Enumerable.Range(1, 20000).Select(i => $"line-{i:D6}-{padding}").ToArray();
        File.WriteAllLines(log, lines);

        var text = diagnostic_bundle.read_log_full(log, 1024 * 1024);

        Assert.StartsWith("[truncated: showing last 1.0 MB of ", text);
        Assert.Contains("lines dropped]", text);
        Assert.DoesNotContain("line-000001-", text);
        Assert.Contains("line-020000-", text);
    }

    [Fact]
    public void redact_pii_replaces_current_username()
    {
        var input = "2026-08-18 12:00:00 user marb1 opened profile FPS\r\n";
        var text = diagnostic_bundle.redact_pii_core(input, "marb1", "TESTHOST");

        Assert.DoesNotContain("marb1", text);
        Assert.Contains("<USER>", text);
        Assert.Contains("profile FPS", text);
    }

    [Fact]
    public void redact_pii_normalizes_user_profile_paths()
    {
        var input =
            "config loaded from C:\\Users\\Someone\\AppData\\Roaming\\NvColorProfiles\\config.json\r\n"
            + "another: c:/users/Bob/Documents/x.txt\r\n";
        var text = diagnostic_bundle.redact_pii_core(input, "unused_test_user", "TESTHOST");

        Assert.DoesNotContain("Someone", text);
        Assert.DoesNotContain("Bob", text);
        Assert.Contains(@"C:\Users\<USER>\AppData\Roaming\NvColorProfiles\config.json", text);
        // The forward-slash input keeps its remaining forward slashes after redaction.
        Assert.Contains(@"C:\Users\<USER>/Documents/x.txt", text);
    }

    [Fact]
    public void redact_pii_redacts_user_icc_profile_path()
    {
        var input =
            "      NvCplICCProfile [String] = C:\\Users\\marb1\\Documents\\MyCalibration.icm\r\n";
        var text = diagnostic_bundle.redact_pii_core(input, "marb1", "TESTHOST");

        Assert.DoesNotContain("marb1", text);
        Assert.DoesNotContain(@"C:\Users\<USER>\Documents\MyCalibration.icm", text);
        Assert.Contains(@"<REDACTED>\MyCalibration.icm", text);
    }

    [Fact]
    public void redact_pii_preserves_system_icc_profile_path()
    {
        var input =
            "      NvCplICCProfile [String] = C:\\Windows\\System32\\spool\\drivers\\color\\sRGB Color Space Profile.icm\r\n";
        var text = diagnostic_bundle.redact_pii_core(input, "marb1", "TESTHOST");

        Assert.Contains(@"C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm", text);
        Assert.DoesNotContain("<REDACTED>", text);
    }

    [Fact]
    public void redact_pii_replaces_hostname()
    {
        var input = "Machine name: TESTHOST\r\n";
        var text = diagnostic_bundle.redact_pii_core(input, "unused_test_user", "TESTHOST");

        Assert.DoesNotContain("TESTHOST", text);
        Assert.Contains("<HOSTNAME>", text);
    }

    [Fact]
    public void export_writes_all_expected_entries()
    {
        var config_path = Path.Combine(temp_dir, "config.json");
        File.WriteAllText(config_path, "{\"stub\": true}", new UTF8Encoding(false));

        var log_path = Path.Combine(temp_dir, "test.log");
        File.WriteAllLines(log_path, new[] { "log-line-one", "log-line-two" });

        var inputs = new diagnostic_bundle.inputs(
            app_version: "1.0.2",
            config_file_path: config_path,
            log_file_path: log_path,
            gpu: null);

        var output_dir = Path.Combine(temp_dir, "out");
        var result = diagnostic_bundle.export(inputs, output_dir);

        Assert.True(File.Exists(result.zip_path));
        Assert.True(result.zip_bytes > 0);
        Assert.StartsWith("NvColorProfiles-diagnostic-", Path.GetFileName(result.zip_path));
        Assert.EndsWith(".zip", result.zip_path);

        using var archive = ZipFile.OpenRead(result.zip_path);
        var names = archive.Entries.Select(e => e.FullName).ToHashSet();
        Assert.Contains("README.txt", names);
        Assert.Contains("system.txt", names);
        Assert.Contains("gpu.txt", names);
        Assert.Contains("logs.txt", names);
        Assert.Contains("registry-nvtweak.txt", names);
        Assert.Contains("config.json", names);

        using var config_entry = archive.GetEntry("config.json")!.Open();
        using var config_reader = new StreamReader(config_entry);
        Assert.Contains("\"stub\": true", config_reader.ReadToEnd());

        using var system_entry = archive.GetEntry("system.txt")!.Open();
        using var system_reader = new StreamReader(system_entry);
        var system_text = system_reader.ReadToEnd();
        Assert.Contains("App version: 1.0.2", system_text);
        Assert.Contains(".NET runtime:", system_text);

        using var readme_entry = archive.GetEntry("README.txt")!.Open();
        using var readme_reader = new StreamReader(readme_entry);
        var readme_text = readme_reader.ReadToEnd();
        Assert.Contains("PII-redacted", readme_text);
    }

    [Fact]
    public void export_handles_missing_config_gracefully()
    {
        var log_path = Path.Combine(temp_dir, "test.log");
        File.WriteAllText(log_path, "one line\r\n");

        var inputs = new diagnostic_bundle.inputs(
            app_version: "0.0.0",
            config_file_path: Path.Combine(temp_dir, "no-such-config.json"),
            log_file_path: log_path,
            gpu: null);

        var result = diagnostic_bundle.export(inputs, Path.Combine(temp_dir, "out2"));

        using var archive = ZipFile.OpenRead(result.zip_path);
        var config_entry = archive.GetEntry("config.json");
        Assert.NotNull(config_entry);
        using var reader = new StreamReader(config_entry!.Open());
        Assert.Equal("{}", reader.ReadToEnd().TrimEnd());
    }

    public void Dispose()
    {
        if (Directory.Exists(temp_dir))
        {
            try
            {
                Directory.Delete(temp_dir, recursive: true);
            }
            catch
            {
                // best-effort cleanup; a locked file in the zip must not fail the test
            }
        }
    }
}
