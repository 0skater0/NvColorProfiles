namespace nv_color_profiles.core.updates;

/// <summary>Outcome of one GitHub-releases check. <see cref="error"/> is set on network/parse
/// failures; callers stay silent for the periodic check and surface it in the manual "Check now".</summary>
public sealed record update_check_result(
    string? latest_version,
    string? latest_url,
    bool is_newer,
    DateTime checked_at,
    string? error)
{
    public static update_check_result from_error(string message, DateTime checked_at)
        => new(latest_version: null, latest_url: null, is_newer: false, checked_at: checked_at, error: message);

    public static update_check_result from_found(string latest, string url, bool is_newer, DateTime checked_at)
        => new(latest_version: latest, latest_url: url, is_newer: is_newer, checked_at: checked_at, error: null);
}
