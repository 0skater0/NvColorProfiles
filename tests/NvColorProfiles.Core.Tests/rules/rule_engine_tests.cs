using nv_color_profiles.core.rules;

namespace nv_color_profiles.core.tests.rules;

public class rule_engine_tests
{
    [Fact]
    public void process_rule_matches_with_or_without_exe_and_ignores_case()
    {
        var rules = new[] { new rule { type = match_type.process, value = "EscapeFromTarkov.exe", profile = "Gaming" } };
        Assert.Equal("Gaming", rule_engine.evaluate(rules, "escapefromtarkov", "anything"));
        Assert.Equal("Gaming", rule_engine.evaluate(rules, "EscapeFromTarkov.exe", ""));
    }

    [Fact]
    public void window_title_rule_matches_regex_case_insensitive()
    {
        var rules = new[] { new rule { type = match_type.window_title, value = "(?:youtube|netflix)", profile = "Media" } };
        Assert.Equal("Media", rule_engine.evaluate(rules, "chrome.exe", "Cat videos - YouTube"));
        Assert.Null(rule_engine.evaluate(rules, "chrome.exe", "Some docs"));
    }

    [Fact]
    public void lowest_priority_number_wins_first()
    {
        var rules = new[]
        {
            new rule { priority = 20, type = match_type.process, value = "chrome", profile = "Media" },
            new rule { priority = 10, type = match_type.process, value = "chrome", profile = "Work" },
        };
        Assert.Equal("Work", rule_engine.evaluate(rules, "chrome.exe", ""));
    }

    [Fact]
    public void no_match_returns_null()
    {
        var rules = new[] { new rule { type = match_type.process, value = "game", profile = "Gaming" } };
        Assert.Null(rule_engine.evaluate(rules, "explorer.exe", "Desktop"));
    }

    [Fact]
    public void invalid_regex_does_not_throw_and_does_not_match()
    {
        var rules = new[] { new rule { type = match_type.window_title, value = "(unclosed", profile = "X" } };
        Assert.Null(rule_engine.evaluate(rules, "p", "title"));
    }

    [Fact]
    public void empty_rule_set_returns_null()
    {
        Assert.Null(rule_engine.evaluate(Array.Empty<rule>(), "p", "t"));
    }
}
