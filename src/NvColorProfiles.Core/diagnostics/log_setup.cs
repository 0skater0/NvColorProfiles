using Microsoft.Extensions.Logging;

namespace nv_color_profiles.core.diagnostics;

/// <summary>Builds the application logger factory (file logging into <see cref="app_paths.log_file"/>).</summary>
public static class log_setup
{
    /// <summary>
    /// Creates the logger factory. Pass <see cref="LogLevel.Debug"/> for the diagnostic mode that
    /// records rule evaluation and hardware-apply decisions ("why did profile X win?").
    /// </summary>
    public static ILoggerFactory create_factory(LogLevel min_level)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(min_level);
            builder.AddProvider(new file_logger_provider(app_paths.log_file, min_level));
        });
    }
}
