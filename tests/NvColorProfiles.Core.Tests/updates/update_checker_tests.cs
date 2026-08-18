using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using nv_color_profiles.core.updates;

namespace nv_color_profiles.core.tests.updates;

public sealed class update_checker_tests
{
    [Theory]
    [InlineData("1.2.0", "1.1.0", 1)]
    [InlineData("1.1.0", "1.2.0", -1)]
    [InlineData("1.1.0", "1.1.0", 0)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.1.0", "1.1", 0)]
    [InlineData("1.1.0-rc1", "1.1.0", 0)]
    [InlineData("", "1.0.0", -1)]
    public void compare_versions_orders_numerically(string a, string b, int expected)
    {
        Assert.Equal(expected, update_checker.compare_versions(a, b));
    }

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("V2.0.0", "2.0.0")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("", "")]
    public void normalize_tag_strips_leading_v(string tag, string expected)
    {
        Assert.Equal(expected, update_checker.normalize_tag(tag));
    }

    [Fact]
    public void should_check_returns_true_on_first_run()
    {
        Assert.True(update_checker.should_check(last_check_at: null, DateTime.UtcNow));
    }

    [Fact]
    public void should_check_returns_false_when_within_interval()
    {
        var now = DateTime.UtcNow;
        Assert.False(update_checker.should_check(now.AddHours(-1), now));
    }

    [Fact]
    public void should_check_returns_true_after_interval()
    {
        var now = DateTime.UtcNow;
        Assert.True(update_checker.should_check(now.AddHours(-25), now));
    }

    [Fact]
    public async Task check_returns_error_on_non_success_status()
    {
        var handler = new stub_handler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { ReasonPhrase = "boom" });
        using var http = new HttpClient(handler);
        var checker = new update_checker(http, "owner", "repo", NullLogger<update_checker>.Instance);

        var result = await checker.check_async("1.0.0");

        Assert.NotNull(result.error);
        Assert.False(result.is_newer);
        Assert.Null(result.latest_version);
    }

    [Fact]
    public async Task check_flags_newer_release()
    {
        const string payload = "{\"tag_name\":\"v2.0.0\",\"html_url\":\"https://example/rel/2.0.0\"}";
        var handler = new stub_handler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        using var http = new HttpClient(handler);
        var checker = new update_checker(http, "owner", "repo", NullLogger<update_checker>.Instance);

        var result = await checker.check_async("1.0.0");

        Assert.Null(result.error);
        Assert.True(result.is_newer);
        Assert.Equal("2.0.0", result.latest_version);
        Assert.Equal("https://example/rel/2.0.0", result.latest_url);
    }

    [Fact]
    public async Task check_does_not_flag_same_or_older_release()
    {
        const string payload = "{\"tag_name\":\"v1.0.0\",\"html_url\":\"https://example/rel/1.0.0\"}";
        var handler = new stub_handler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        using var http = new HttpClient(handler);
        var checker = new update_checker(http, "owner", "repo", NullLogger<update_checker>.Instance);

        var result = await checker.check_async("1.0.0");

        Assert.False(result.is_newer);
        Assert.Equal("1.0.0", result.latest_version);
    }

    [Fact]
    public async Task check_falls_back_to_synthetic_url_when_html_url_missing()
    {
        const string payload = "{\"tag_name\":\"v3.1.4\"}";
        var handler = new stub_handler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        using var http = new HttpClient(handler);
        var checker = new update_checker(http, "owner", "repo", NullLogger<update_checker>.Instance);

        var result = await checker.check_async("1.0.0");

        Assert.True(result.is_newer);
        Assert.Equal("https://github.com/owner/repo/releases/tag/v3.1.4", result.latest_url);
    }

    private sealed class stub_handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler;
        public stub_handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) => this.handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request, cancellationToken));
    }
}
