using System.Text;
using Microsoft.Extensions.Logging;

namespace nv_color_profiles.core.diagnostics;

/// <summary>Minimal <see cref="ILogger"/> that formats one line per entry into the shared writer.</summary>
internal sealed class file_logger : ILogger
{
    private readonly string category;
    private readonly file_log_writer writer;
    private readonly LogLevel min_level;

    public file_logger(string category, file_log_writer writer, LogLevel min_level)
    {
        this.category = short_category(category);
        this.writer = writer;
        this.min_level = min_level;
    }

    public IDisposable? BeginScope<t_state>(t_state state) where t_state : notnull => null;

    public bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= min_level;

    public void Log<t_state>(
        LogLevel level,
        EventId event_id,
        t_state state,
        Exception? exception,
        Func<t_state, Exception?, string> formatter)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        builder.Append(" [").Append(level_label(level)).Append("] ");
        builder.Append(category).Append(" - ").Append(formatter(state, exception));
        if (exception is not null)
        {
            builder.Append(Environment.NewLine).Append(exception);
        }

        writer.write_line(builder.ToString());
    }

    private static string level_label(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    /// <summary>Strip the namespace prefix so log lines stay readable (e.g. "display_controller").</summary>
    private static string short_category(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot >= 0 && dot < category.Length - 1 ? category[(dot + 1)..] : category;
    }
}
