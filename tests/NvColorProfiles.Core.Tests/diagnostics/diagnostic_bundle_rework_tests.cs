using System.IO.Compression;
using System.Text;
using nv_color_profiles.core.diagnostics;

namespace nv_color_profiles.core.tests.diagnostics;

/// <summary>
/// Tests for the diagnostic-bundle sections added in the 1.2 rework: triage summary, per-display
/// enrichment in the GPU report, third-party color-tool report, NVCP plain-text report, and the
/// summary detection of a stale active profile relative to what the log last applied.
/// </summary>
public sealed class diagnostic_bundle_rework_tests : IDisposable
{
    private readonly string temp_dir;

    public diagnostic_bundle_rework_tests()
    {
        temp_dir = Path.Combine(Path.GetTempPath(), "nvcp_diag_rework_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp_dir);
    }

    [Fact]
    public void summary_reports_mode_active_profile_and_counts()
    {
        var inputs = new diagnostic_bundle.inputs(
            app_version: "1.2.0",
            config_file_path: "ignored",
            log_file_path: "ignored",
            gpu: null)
        {
            app_mode = "manual",
            active_profile = "Tarkov",
            profile_count = 3,
            rule_count = 2,
            schedule_count = 1,
            gamma_available = true,
        };

        var text = diagnostic_bundle.build_summary_report(inputs, log_text: "");

        Assert.Contains("App version:      1.2.0", text);
        Assert.Contains("Switching mode:   manual", text);
        Assert.Contains("Active profile:   Tarkov", text);
        Assert.Contains("Profiles/rules/schedules: 3 / 2 / 1", text);
        Assert.Contains("Gamma backend:    available", text);
        Assert.Contains("(none in current log)", text);
    }

    [Fact]
    public void summary_extracts_last_applied_profile_from_log()
    {
        var log =
            "2026-08-19 00:49:35.408 [INF] profile_service - Applied profile 'Default' to 2 display(s)\r\n" +
            "2026-08-19 00:52:39.005 [INF] profile_service - Applied profile 'Default' to 2 display(s)\r\n" +
            "2026-08-19 00:53:11.221 [INF] profile_service - Applied profile 'Tarkov' to 2 display(s)\r\n";

        var inputs = new diagnostic_bundle.inputs("1.2.0", "x", "x", gpu: null)
        {
            app_mode = "automatic",
            active_profile = "Tarkov",
            gamma_available = true,
        };

        var text = diagnostic_bundle.build_summary_report(inputs, log);

        Assert.Contains("'Tarkov'", text);
        Assert.Contains("2026-08-19 00:53:11.221", text);
        Assert.DoesNotContain("differs from the active profile", text);
    }

    [Fact]
    public void summary_flags_divergence_when_log_last_applied_differs_from_active_profile()
    {
        var log =
            "2026-08-19 00:49:35.408 [INF] profile_service - Applied profile 'Default' to 2 display(s)\r\n";

        var inputs = new diagnostic_bundle.inputs("1.2.0", "x", "x", gpu: null)
        {
            app_mode = "manual",
            active_profile = "Tarkov",   // configured active, but the log never actually applied it
            gamma_available = true,
        };

        var text = diagnostic_bundle.build_summary_report(inputs, log);

        Assert.Contains("'Default'", text);
        Assert.Contains("differs from the active profile", text);
    }

    [Fact]
    public void gpu_report_includes_friendly_name_edid_hdr_icc_and_current_dv_hue()
    {
        var display = new diagnostic_bundle.display_entry(0x80000001u, 0xDEADBEEFu, @"\\.\DISPLAY1")
        {
            friendly_name = "LG UltraGear 34GN850",
            edid_vendor = "GSM",
            edid_product_code = 0x5B34,
            current_vibrance = 100,
            current_hue = 0,
            icc_profile = "sRGB Color Space Profile.icm",
        };
        var snapshot = new diagnostic_bundle.gpu_snapshot(
            driver_version: "610.88",
            driver_branch: "r610_85",
            gpu_names: new[] { "NVIDIA RTX 5090" },
            displays: new[] { display });

        var text = diagnostic_bundle.build_gpu_report(snapshot);

        Assert.Contains("LG UltraGear 34GN850", text);
        Assert.Contains("gdi: \\\\.\\DISPLAY1", text);
        Assert.Contains("displayId=0x80000001", text);
        Assert.Contains("luid=0xDEADBEEF", text);
        Assert.Contains("edid: vendor=GSM", text);
        Assert.Contains("product=0x5B34", text);
        Assert.Contains("sRGB Color Space Profile.icm", text);
        Assert.Contains("current vibrance/hue: 100 / 0", text);
    }

    [Fact]
    public void gpu_report_still_works_with_only_the_base_fields()
    {
        // Ensures the pre-rework constructor call site (three-arg display_entry, no enrichment)
        // does not break the report — a bug bundle produced without the new probes must still render.
        var snapshot = new diagnostic_bundle.gpu_snapshot(
            driver_version: "610.88",
            driver_branch: "r610_85",
            gpu_names: new[] { "NVIDIA RTX 5090" },
            displays: new[] { new diagnostic_bundle.display_entry(0x80000002u, null, @"\\.\DISPLAY2") });

        var text = diagnostic_bundle.build_gpu_report(snapshot);

        Assert.Contains("displayId=0x80000002", text);
        Assert.Contains("(unresolved)", text);
        Assert.DoesNotContain("edid:", text);
        Assert.DoesNotContain("current vibrance/hue:", text);
        // ICC is reported for every display now, with a "system default (sRGB)" fallback so
        // the reader can tell we probed and found nothing custom.
        Assert.Contains("ICC profile: system default (sRGB)", text);
    }

    [Fact]
    public void color_tools_report_has_a_header_and_lists_empty_state()
    {
        var text = diagnostic_bundle.build_color_tools_report();

        Assert.Contains("Known color-related third-party tools", text);
        // On the test host we cannot assume any particular tool is running; the report must at
        // least render a valid empty-state or list section.
        Assert.True(text.Contains("(none detected)") || text.Contains("- "),
            $"expected either the empty marker or at least one hit line, got:\n{text}");
    }

    [Fact]
    public void nvcp_report_renders_without_throwing_and_has_a_header()
    {
        var text = diagnostic_bundle.build_nvcp_report();

        // Content depends on whether the host has NVCP installed; only the structural bits must
        // be there so the entry in the bundle is always parseable.
        Assert.True(
            text.Contains("NVIDIA Control Panel color state per device")
            || text.Contains("(NVIDIA Control Panel state only available on Windows)"),
            $"expected a header or non-Windows stub, got:\n{text}");
    }

    [Fact]
    public void export_writes_the_new_bundle_entries()
    {
        var config_path = Path.Combine(temp_dir, "config.json");
        File.WriteAllText(config_path, "{\"stub\": true}", new UTF8Encoding(false));
        var log_path = Path.Combine(temp_dir, "test.log");
        File.WriteAllText(log_path,
            "2026-08-19 00:49:35.408 [INF] profile_service - Applied profile 'Default' to 1 display(s)\r\n");

        var inputs = new diagnostic_bundle.inputs("1.2.0", config_path, log_path, gpu: null)
        {
            app_mode = "manual",
            active_profile = "Default",
            profile_count = 1,
            rule_count = 0,
            schedule_count = 0,
            gamma_available = true,
        };

        var result = diagnostic_bundle.export(inputs, Path.Combine(temp_dir, "out"));

        using var archive = ZipFile.OpenRead(result.zip_path);
        var names = archive.Entries.Select(e => e.FullName).ToHashSet();
        Assert.Contains("summary.txt", names);
        Assert.Contains("nvcp-state.txt", names);
        Assert.Contains("color-tools.txt", names);
        Assert.Contains("README.txt", names);
        Assert.Contains("system.txt", names);
        Assert.Contains("gpu.txt", names);
        Assert.Contains("logs.txt", names);
        Assert.Contains("registry-nvtweak.txt", names);
        Assert.Contains("config.json", names);

        using var summary_entry = archive.GetEntry("summary.txt")!.Open();
        using var summary_reader = new StreamReader(summary_entry);
        var summary_text = summary_reader.ReadToEnd();
        Assert.Contains("App version:      1.2.0", summary_text);
        Assert.Contains("Switching mode:   manual", summary_text);
    }

    public void Dispose()
    {
        if (Directory.Exists(temp_dir))
        {
            try { Directory.Delete(temp_dir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
