using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.OnDemand.Seerr;

public record SeerrUser(int Id, string? JellyfinUserId, int Permissions);

/// <summary>Calls Seerr with the server-side API key, impersonating a user via X-API-User.</summary>
public class SeerrClient
{
    private readonly HttpClient _http;

    public SeerrClient(HttpClient http, string baseUrl, string apiKey)
    {
        _http = http;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/api/v1/");
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    private HttpRequestMessage Req(HttpMethod m, string url, int? asUser)
    {
        var r = new HttpRequestMessage(m, url);
        if (asUser is int id) r.Headers.Add("X-API-User", id.ToString());
        return r;
    }

    /// <summary>Option A from the plan: find the Seerr user for a Jellyfin user id.</summary>
    public async Task<SeerrUser?> FindUserByJellyfinIdAsync(Guid jellyfinUserId, CancellationToken ct)
    {
        var want = jellyfinUserId.ToString("N");
        for (var skip = 0; ; skip += 50)
        {
            using var resp = await _http.SendAsync(Req(HttpMethod.Get, $"user?take=50&skip={skip}", null), ct);
            resp.EnsureSuccessStatusCode();
            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!;
            var results = json["results"]!.AsArray();
            foreach (var u in results)
            {
                var jf = u!["jellyfinUserId"]?.GetValue<string>();
                if (jf != null && Guid.TryParse(jf, out var g) && g == jellyfinUserId)
                    return new SeerrUser(u["id"]!.GetValue<int>(), jf, u["permissions"]?.GetValue<int>() ?? 0);
            }
            if (results.Count < 50) return null;
        }
    }

    public async Task<JsonNode?> SearchAsync(int seerrUserId, string query, CancellationToken ct)
    {
        using var resp = await _http.SendAsync(
            Req(HttpMethod.Get, $"search?query={Uri.EscapeDataString(query)}&page=1", seerrUserId), ct);
        resp.EnsureSuccessStatusCode();
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    public async Task<JsonNode?> RequestMovieAsync(int seerrUserId, int tmdbId, CancellationToken ct)
    {
        var req = Req(HttpMethod.Post, "request", seerrUserId);
        req.Content = JsonContent.Create(new { mediaType = "movie", mediaId = tmdbId });
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    public async Task<JsonNode?> GetStatusAsync(CancellationToken ct) =>
        JsonNode.Parse(await _http.GetStringAsync("../api/v1/status".Replace("../", ""), ct));
}

public static class SeerrPermissions
{
    // Seerr bitmask: ADMIN=2, REQUEST=32, AUTO_APPROVE=128, AUTO_APPROVE_MOVIE=256
    public static bool AutoApprovesMovies(int permissions) =>
        (permissions & 2) != 0 || (permissions & 128) != 0 || (permissions & 256) != 0;
}
