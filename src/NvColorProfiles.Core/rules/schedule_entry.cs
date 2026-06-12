namespace nv_color_profiles.core.rules;

/// <summary>
/// A time-of-day window that selects a profile in auto mode when no app rule matches. Times are
/// "HH:mm"; if <see cref="to"/> is earlier than <see cref="from"/> the window wraps past midnight.
/// </summary>
public sealed record schedule_entry
{
    public string from { get; init; } = "00:00";
    public string to { get; init; } = "00:00";
    public string profile { get; init; } = "Default";
}
