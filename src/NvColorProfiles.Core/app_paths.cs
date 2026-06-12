namespace nv_color_profiles.core;

/// <summary>
/// Resolves the on-disk locations for config and logs. Two modes:
/// <list type="bullet">
///   <item>Portable — a <c>config.json</c> sits next to the executable; everything lives there.</item>
///   <item>Roaming — nothing next to the exe; config and logs go to %APPDATA%\NvColorProfiles.</item>
/// </list>
/// The portable heuristic is intentionally simple here; an explicit setting could refine it later.
/// </summary>
public static class app_paths
{
    public const string CONFIG_FILE_NAME = "config.json";
    public const string LOG_DIR_NAME = "logs";
    public const string LOG_FILE_NAME = "nvcolorprofiles.log";

    static app_paths()
    {
        executable_dir = AppContext.BaseDirectory;

        var portable_config = Path.Combine(executable_dir, CONFIG_FILE_NAME);
        is_portable = File.Exists(portable_config);

        base_dir = is_portable
            ? executable_dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), app_info.CONFIG_DIR_NAME);

        config_file = Path.Combine(base_dir, CONFIG_FILE_NAME);
        log_dir = Path.Combine(base_dir, LOG_DIR_NAME);
        log_file = Path.Combine(log_dir, LOG_FILE_NAME);
    }

    /// <summary>Directory the running executable lives in.</summary>
    public static string executable_dir { get; }

    /// <summary>True when running in portable mode (config next to the exe).</summary>
    public static bool is_portable { get; }

    /// <summary>Base directory for config and logs (exe dir when portable, else %APPDATA%\NvColorProfiles).</summary>
    public static string base_dir { get; }

    public static string config_file { get; }
    public static string log_dir { get; }
    public static string log_file { get; }
}
