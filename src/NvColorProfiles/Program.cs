using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using nv_color_profiles.app;
using nv_color_profiles.core.diagnostics;

namespace nv_color_profiles;

internal static class program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // single instance — a second launch just exits
        using var mutex = new Mutex(initiallyOwned: true, @"Local\NvColorProfiles.SingleInstance", out var is_owner);
        if (!is_owner)
        {
            return 0;
        }

        // --check: initialise everything (incl. baseline capture) and exit WITHOUT a UI.
        // Non-destructive self-test for install/CI verification.
        if (args.Contains("--check"))
        {
            using var loggers = log_setup.create_factory(LogLevel.Information);
            var log = loggers.CreateLogger("startup");
            using var probe = new app_host(loggers);
            log.LogInformation("Self-check OK (nvapi={available})", probe.nvapi_available);
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<nv_app>()
            .UsePlatformDetect()
            .LogToTrace();
}
