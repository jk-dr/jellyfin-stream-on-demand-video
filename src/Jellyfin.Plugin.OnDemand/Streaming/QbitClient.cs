using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.OnDemand.Streaming;

public record TorrentInfo(string Hash, string State, long Size, double Progress, long DlSpeed, long PieceSize, string ContentPath);

public class QbitClient
{
    private readonly HttpClient _http;
    private readonly string _user, _pass;
    private bool _loggedIn;

    public QbitClient(HttpClient http, string baseUrl, string user, string pass)
    {
        _http = http; _user = user; _pass = pass;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/api/v2/");
    }

    private async Task LoginAsync(CancellationToken ct)
    {
        using var resp = await _http.PostAsync("auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["username"] = _user, ["password"] = _pass }), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode || !body.Contains("Ok")) throw new InvalidOperationException("qBittorrent login failed");
        _loggedIn = true;
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        if (!_loggedIn) await LoginAsync(ct);
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            await LoginAsync(ct);
            resp = await _http.GetAsync(url, ct);
        }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public async Task<TorrentInfo?> GetTorrentAsync(string hash, CancellationToken ct)
    {
        var arr = JsonNode.Parse(await GetAsync($"torrents/info?hashes={hash}", ct))!.AsArray();
        if (arr.Count == 0) return null;
        var t = arr[0]!;
        var props = JsonNode.Parse(await GetAsync($"torrents/properties?hash={hash}", ct))!;
        return new TorrentInfo(hash, t["state"]!.GetValue<string>(), t["size"]!.GetValue<long>(),
            t["progress"]!.GetValue<double>(), t["dlspeed"]!.GetValue<long>(),
            props["piece_size"]!.GetValue<long>(), t["content_path"]!.GetValue<string>());
    }

    public async Task<PieceMap?> GetPieceMapAsync(string hash, CancellationToken ct)
    {
        var info = await GetTorrentAsync(hash, ct);
        if (info == null) return null;
        var states = JsonNode.Parse(await GetAsync($"torrents/pieceStates?hash={hash}", ct))!
            .AsArray().Select(n => n!.GetValue<int>()).ToArray();
        return new PieceMap(states, info.PieceSize, info.Size);
    }
}
