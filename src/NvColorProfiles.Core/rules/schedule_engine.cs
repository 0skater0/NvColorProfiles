using System.Globalization;

namespace nv_color_profiles.core.rules;

/// <summary>Resolves the active time-of-day profile. Stateless; the app polls it on a timer.</summary>
public static class schedule_engine
{
    /// <summary>
    /// Profile of the first schedule whose window contains <paramref name="now"/>, or null when none
    /// match. Windows are [from, to); a window with to &lt; from wraps past midnight.
    /// </summary>
    public static string? evaluate(IReadOnlyList<schedule_entry> schedules, TimeOnly now)
    {
        foreach (var s in schedules)
        {
            if (!try_parse(s.from, out var from) || !try_parse(s.to, out var to) || from == to)
            {
                continue; // unparsable or empty window
            }
            var inside = from < to
                ? now >= from && now < to
                : now >= from || now < to; // wraps past midnight
            if (inside)
            {
                return s.profile;
            }
        }
        return null;
    }

    private static bool try_parse(string value, out TimeOnly time)
        => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
