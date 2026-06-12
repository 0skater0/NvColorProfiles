namespace nv_color_profiles.core.diagnostics;

/// <summary>
/// Thread-safe append writer for the log file, with size-based rotation
/// (one rolled-over backup is kept). Low log volume → open/append/close per line is fine.
/// </summary>
internal sealed class file_log_writer
{
    private const long DEFAULT_MAX_BYTES = 5 * 1024 * 1024;

    private readonly object gate = new();
    private readonly string log_file;
    private readonly long max_bytes;

    public file_log_writer(string log_file, long max_bytes = DEFAULT_MAX_BYTES)
    {
        this.log_file = log_file;
        this.max_bytes = max_bytes;
        Directory.CreateDirectory(Path.GetDirectoryName(log_file)!);
    }

    public void write_line(string line)
    {
        // Logging is best-effort: a full disk, locked file or missing path must never
        // propagate an exception into the calling (production) code path.
        lock (gate)
        {
            try
            {
                rotate_if_needed();
                File.AppendAllText(log_file, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // swallow — losing a log line is acceptable, crashing the app is not
            }
        }
    }

    private void rotate_if_needed()
    {
        var info = new FileInfo(log_file);
        if (!info.Exists || info.Length <= max_bytes)
        {
            return;
        }

        var rolled = log_file + ".1";
        if (File.Exists(rolled))
        {
            File.Delete(rolled);
        }
        File.Move(log_file, rolled);
    }
}
