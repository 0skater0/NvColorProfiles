namespace nv_color_profiles.core;

/// <summary>
/// Static metadata about the application, shared between the headless core and the UI.
/// </summary>
public static class app_info
{
    public const string APP_NAME = "NvColorProfiles";

    /// <summary>Folder name used for portable config and per-user (%APPDATA%) config alike.</summary>
    public const string CONFIG_DIR_NAME = "NvColorProfiles";
}
