using Microsoft.Extensions.Logging;

namespace nv_color_profiles.core.diagnostics;

/// <summary><see cref="ILoggerProvider"/> backing every logger with one shared file writer.</summary>
public sealed class file_logger_provider : ILoggerProvider
{
    private readonly file_log_writer writer;
    private readonly LogLevel min_level;

    public file_logger_provider(string log_file, LogLevel min_level)
    {
        this.writer = new file_log_writer(log_file);
        this.min_level = min_level;
    }

    public ILogger CreateLogger(string category_name) => new file_logger(category_name, writer, min_level);

    public void Dispose()
    {
        // file_log_writer holds no persistent handles (open/append/close per line).
    }
}
