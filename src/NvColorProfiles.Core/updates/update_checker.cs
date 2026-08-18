using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace nv_color_profiles.core.updates;

/// <summary>
/// Fetches the newest GitHub release for the configured owner/repo and compares its <c>tag_name</c>
/// to the running assembly version. Silent by design: any failure returns an error-carrying result
/// so callers keep quiet on the periodic check and can surface the message on manual runs.
/// </summary>
public sealed class update_checker
{
    /// <summary>Poll cadence enforced by <see cref="should_check"/>: at most once every 24 hours.</summary>
    public static readonly TimeSpan CHECK_INTERVAL = TimeSpan.FromHours(24);

    private const string DEFAULT_OWNER = "0skater0";
    private const string DEFAULT_REPO = "NvColorProfiles";

    private readonly ILogger<update_checker> log;
    private readonly HttpClient http;
    private readonly string owner;
    private readonly string repo;
    private readonly bool owns_http;

    /// <summary>Production constructor: builds an HttpClient with a 10s timeout and the required
    /// GitHub User-Agent (anonymous requests without a UA are rejected by the GitHub API).</summary>
    public update_checker(string current_version, ILogger<update_checker> log, string owner = DEFAULT_OWNER, string repo = DEFAULT_REPO)
        : this(build_client(current_version), owner, repo, log, owns_http: true)
    {
    }

    /// <summary>Test seam: pass a preconfigured <see cref="HttpClient"/> (custom handler, base URL).</summary>
    internal update_checker(HttpClient http, string owner, string repo, ILogger<update_checker> log, bool owns_http = false)
    {
        this.http = http;
        this.owner = owner;
        this.repo = repo;
        this.log = log;
        this.owns_http = owns_http;
    }

    /// <summary>True when the periodic scheduler should fire another check. Both a missing timestamp
    /// and a stamp older than <see cref="CHECK_INTERVAL"/> yield true.</summary>
    public static bool should_check(DateTime? last_check_at, DateTime now)
        => last_check_at is null || (now - last_check_at.Value) >= CHECK_INTERVAL;

    /// <summary>Runs one check. Never throws; failures come back on <see cref="update_check_result.error"/>.</summary>
    public async Task<update_check_result> check_async(string current_version, CancellationToken cancellation = default)
    {
        var checked_at = DateTime.UtcNow;
        try
        {
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            using var response = await http.GetAsync(url, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var message = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                log.LogInformation("Update check failed: {message}", message);
                return update_check_result.from_error(message, checked_at);
            }
            var body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag_element))
            {
                return update_check_result.from_error("release payload missing 'tag_name'", checked_at);
            }
            var tag = tag_element.GetString() ?? string.Empty;
            var html_url = root.TryGetProperty("html_url", out var html_element) ? html_element.GetString() : null;
            var release_url = html_url ?? $"https://github.com/{owner}/{repo}/releases/tag/{tag}";

            var latest_version = normalize_tag(tag);
            var is_newer = compare_versions(latest_version, current_version) > 0;
            return update_check_result.from_found(latest_version, release_url, is_newer, checked_at);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogInformation(ex, "Update check failed");
            return update_check_result.from_error(ex.Message, checked_at);
        }
    }

    /// <summary>Strips a leading "v" from a GitHub tag ("v1.2.0" -> "1.2.0").</summary>
    public static string normalize_tag(string tag)
        => tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;

    /// <summary>Compares two dotted-version strings numerically per component; unparseable segments
    /// count as 0. Returns 1 when <paramref name="a"/> is newer than <paramref name="b"/>, -1 if
    /// older, 0 if equal. Ignores anything after a "-" pre-release suffix.</summary>
    public static int compare_versions(string a, string b)
    {
        var parts_a = split(a);
        var parts_b = split(b);
        var max = Math.Max(parts_a.Length, parts_b.Length);
        for (var i = 0; i < max; i++)
        {
            var va = i < parts_a.Length ? parts_a[i] : 0;
            var vb = i < parts_b.Length ? parts_b[i] : 0;
            if (va != vb)
            {
                return va > vb ? 1 : -1;
            }
        }
        return 0;
    }

    private static int[] split(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return Array.Empty<int>();
        }
        var trimmed = version.Trim();
        // drop pre-release suffix ("1.2.0-rc1" -> "1.2.0") — release comparisons only need the core
        var dash = trimmed.IndexOf('-');
        if (dash > 0)
        {
            trimmed = trimmed[..dash];
        }
        var pieces = trimmed.Split('.');
        var result = new int[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
        {
            result[i] = int.TryParse(pieces[i], out var n) ? n : 0;
        }
        return result;
    }

    private static HttpClient build_client(string version)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub rejects anonymous requests without a User-Agent header
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"NvColorProfiles/{version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public void dispose()
    {
        if (owns_http)
        {
            http.Dispose();
        }
    }
}
