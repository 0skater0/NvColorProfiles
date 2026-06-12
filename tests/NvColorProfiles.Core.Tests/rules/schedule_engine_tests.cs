using nv_color_profiles.core.rules;

namespace nv_color_profiles.core.tests.rules;

public class schedule_engine_tests
{
    [Fact]
    public void daytime_window_matches_inside_and_excludes_the_end()
    {
        var schedules = new[] { new schedule_entry { from = "09:00", to = "17:00", profile = "Work" } };
        Assert.Equal("Work", schedule_engine.evaluate(schedules, new TimeOnly(9, 0)));
        Assert.Equal("Work", schedule_engine.evaluate(schedules, new TimeOnly(12, 30)));
        Assert.Null(schedule_engine.evaluate(schedules, new TimeOnly(17, 0))); // end is exclusive
        Assert.Null(schedule_engine.evaluate(schedules, new TimeOnly(8, 59)));
    }

    [Fact]
    public void overnight_window_wraps_past_midnight()
    {
        var schedules = new[] { new schedule_entry { from = "22:00", to = "06:00", profile = "Night" } };
        Assert.Equal("Night", schedule_engine.evaluate(schedules, new TimeOnly(23, 30)));
        Assert.Equal("Night", schedule_engine.evaluate(schedules, new TimeOnly(2, 0)));
        Assert.Equal("Night", schedule_engine.evaluate(schedules, new TimeOnly(22, 0)));
        Assert.Null(schedule_engine.evaluate(schedules, new TimeOnly(6, 0)));
        Assert.Null(schedule_engine.evaluate(schedules, new TimeOnly(12, 0)));
    }

    [Fact]
    public void first_matching_schedule_wins()
    {
        var schedules = new[]
        {
            new schedule_entry { from = "20:00", to = "23:00", profile = "Evening" },
            new schedule_entry { from = "21:00", to = "22:00", profile = "LateEvening" },
        };
        Assert.Equal("Evening", schedule_engine.evaluate(schedules, new TimeOnly(21, 30)));
    }

    [Fact]
    public void empty_or_unparsable_windows_are_skipped()
    {
        var schedules = new[]
        {
            new schedule_entry { from = "10:00", to = "10:00", profile = "Empty" },
            new schedule_entry { from = "nonsense", to = "12:00", profile = "Bad" },
        };
        Assert.Null(schedule_engine.evaluate(schedules, new TimeOnly(10, 0)));
        Assert.Null(schedule_engine.evaluate(schedules, new TimeOnly(11, 0)));
    }

    [Fact]
    public void no_schedules_returns_null()
        => Assert.Null(schedule_engine.evaluate(Array.Empty<schedule_entry>(), new TimeOnly(12, 0)));
}
