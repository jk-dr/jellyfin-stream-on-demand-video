using System.Net.Mime;
using Jellyfin.Plugin.OnDemand.Seerr;
using Jellyfin.Plugin.OnDemand.State;
using Jellyfin.Plugin.OnDemand.Streaming;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Jellyfin.Plugin.OnDemand.Api;

[ApiController]
[Route("OnDemand")]
[Produces(MediaTypeNames.Application.Json)]
public class OnDemandController : ControllerBase
{
    private readonly IHttpClientFactory _http;
    private readonly StateStore _state;

    public OnDemandController(IHttpClientFactory http, StateStore state)
    {
        _http = http; _state = state;
    }

    private static PluginConfiguration Cfg => Plugin.Instance!.Configuration;
    private SeerrClient Seerr() => new(_http.CreateClient(), Cfg.SeerrUrl, Cfg.SeerrApiKey);
    private QbitClient Qbit() => new(_http.CreateClient(), Cfg.QbitUrl, Cfg.QbitUsername, Cfg.QbitPassword);

    /// <summary>The Jellyfin user comes from the auth token, never from the request.</summary>
    private Guid? CallerId()
    {
        var v = User.FindFirstValue("Jellyfin-UserId");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    [HttpGet("Search")]
    [Authorize]
    public async Task<ActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        if (CallerId() is not Guid uid) return Unauthorized();
        var su = await Seerr().FindUserByJellyfinIdAsync(uid, ct);
        if (su == null) return NotFound(new { error = "no Seerr user for this Jellyfin user" });
        var res = await Seerr().SearchAsync(su.Id, q, ct);
        return Content(res?.ToJsonString() ?? "{}", "application/json");
    }

    [HttpGet("Me")]
    [Authorize]
    public async Task<ActionResult> Me(CancellationToken ct)
    {
        if (CallerId() is not Guid uid) return Unauthorized();
        var su = await Seerr().FindUserByJellyfinIdAsync(uid, ct);
        if (su == null) return NotFound();
        return Ok(new { seerrUserId = su.Id, canPlayNow = SeerrPermissions.AutoApprovesMovies(su.Permissions) });
    }

    [HttpPost("Play/{tmdbId:int}")]
    [Authorize]
    public async Task<ActionResult> Play(int tmdbId, CancellationToken ct)
    {
        if (CallerId() is not Guid uid) return Unauthorized();
        var su = await Seerr().FindUserByJellyfinIdAsync(uid, ct);
        if (su == null) return NotFound();
        if (!SeerrPermissions.AutoApprovesMovies(su.Permissions))
            return StatusCode(403, new { error = "Play now needs movie auto-approve; request only" });
        if (_state.Get(tmdbId) is { Status: not (MovieStatus.Failed or MovieStatus.Cancelled) } existing)
            return Ok(existing);
        await Seerr().RequestMovieAsync(su.Id, tmdbId, ct);
        return Ok(_state.Upsert(tmdbId, s => { s.Status = MovieStatus.Requested; s.RequestedBy = uid; }));
    }

    [HttpGet("Status/{tmdbId:int}")]
    [Authorize]
    public async Task<ActionResult> Status(int tmdbId, CancellationToken ct)
    {
        var s = _state.Get(tmdbId);
        if (s == null) return NotFound();
        if (s.TorrentHash == null) return Ok(new { s.Status, gate = (object?)null });
        var qb = Qbit();
        var map = await qb.GetPieceMapAsync(s.TorrentHash, ct);
        var info = await qb.GetTorrentAsync(s.TorrentHash, ct);
        if (map == null || info == null) return Ok(new { s.Status, gate = (object?)null });
        var gate = BufferGate.Evaluate(new GateInput(map, info.DlSpeed, EstimatedDuration(s)),
            Cfg.MinBufferFraction, Cfg.MinSpeedFactor);
        return Ok(new { s.Status, gate, info.Progress });
    }

    // TODO(Phase 2): take runtime from Jellyfin/TMDB; 2h default per plan.
    private static double EstimatedDuration(MovieState _) => 7200;

    /// <summary>Gated, Range-capable stream of the in-progress file.</summary>
    [HttpGet("Stream/{tmdbId:int}")]
    [Authorize]
    public async Task Stream(int tmdbId, CancellationToken ct)
    {
        var s = _state.Get(tmdbId);
        if (s?.TorrentHash == null) { Response.StatusCode = 404; return; }
        var qb = Qbit();
        var info = await qb.GetTorrentAsync(s.TorrentHash, ct);
        if (info == null || !System.IO.File.Exists(info.ContentPath)) { Response.StatusCode = 404; return; }

        var total = info.Size;
        long start = 0, end = total - 1;
        var partial = false;
        if (Request.Headers.Range.Count > 0 &&
            System.Net.Http.Headers.RangeHeaderValue.TryParse(Request.Headers.Range!, out var rh) &&
            rh.Ranges.FirstOrDefault() is { } r)
        {
            partial = true;
            if (r.From is null) { start = Math.Max(0, total - (r.To ?? 0)); }
            else { start = r.From.Value; end = Math.Min(total - 1, r.To ?? total - 1); }
            if (start >= total || end < start) { Response.StatusCode = 416; return; }
        }

        // Serve only what is contiguous & complete; wait for the first piece(s) of the request.
        var firstEnd = Math.Min(end, start + 1);
        if (!await GatedFileReader.WaitForRangeAsync(c => qb.GetPieceMapAsync(s.TorrentHash, c),
                start, firstEnd, TimeSpan.FromSeconds(30), ct))
        { Response.StatusCode = 503; Response.Headers.RetryAfter = "5"; return; }

        var map = (await qb.GetPieceMapAsync(s.TorrentHash, ct))!;
        end = Math.Min(end, start + map.ContiguousFrom(start) - 1);

        Response.StatusCode = partial ? 206 : 200;
        Response.ContentType = "video/mp4";
        Response.Headers.AcceptRanges = "bytes";
        Response.Headers.ContentRange = $"bytes {start}-{end}/{total}";
        Response.ContentLength = end - start + 1;
        await GatedFileReader.CopyRangeAsync(info.ContentPath, start, end - start + 1, Response.Body, ct);
    }

    /// <summary>Radarr webhook (On Grab / On Import). Auth is a shared token, not a Jellyfin login.</summary>
    [HttpPost("Webhook/Radarr")]
    [AllowAnonymous]
    public ActionResult RadarrWebhook([FromQuery] string token, [FromBody] System.Text.Json.Nodes.JsonNode body)
    {
        if (token != Cfg.WebhookToken) return Unauthorized();
        var type = body["eventType"]?.GetValue<string>();
        var tmdb = body["movie"]?["tmdbId"]?.GetValue<int>();
        if (tmdb is not int id) return Ok();
        switch (type)
        {
            case "Grab":
                var hash = body["downloadId"]?.GetValue<string>();
                var folder = body["movie"]?["folderPath"]?.GetValue<string>();
                _state.Upsert(id, s =>
                {
                    s.TorrentHash = hash?.ToLowerInvariant(); s.Status = MovieStatus.Preparing;
                    s.RadarrMovieId = body["movie"]?["id"]?.GetValue<int>();
                    if (folder != null)
                    {
                        Directory.CreateDirectory(folder);
                        s.StubPath = Path.Combine(folder, "ondemand-stub.strm");
                        System.IO.File.WriteAllText(s.StubPath, $"http://localhost:8096/OnDemand/Stream/{id}");
                    }
                });
                break;
            case "Download": // On Import
                _state.Upsert(id, s =>
                {
                    if (s.StubPath != null && System.IO.File.Exists(s.StubPath)) System.IO.File.Delete(s.StubPath);
                    s.StubPath = null; s.Status = MovieStatus.InLibrary;
                });
                break;
        }
        return Ok();
    }
}
