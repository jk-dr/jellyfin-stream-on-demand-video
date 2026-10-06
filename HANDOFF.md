# Handoff: Jellyfin On-Demand plugin

Design source: "Jellyfin On-Demand Plugin Plan" (docx, not in repo). Summary of it:
press play on any Seerr *movie* and watch while it downloads (qBittorrent sequential + gated Range
endpoint). Shows are request-only (Sonarr). Radarr locked to 1080p ~1.4 GiB. Private build, GPL-3.0 if shared.
Target: Jellyfin 10.11.11, .NET 9. Branch: `claude/plugin-dev-cloud-test-uj0dfe`. No PR opened.

## Decisions already made
qBittorrent; Jellyfin Enhanced already installed (covers web Seerr search/requests); Abyss theme (Branding custom CSS);
Play now only for users whose Seerr profile auto-approves movies; 30-day retention for unwatched on-demand movies;
every client incl. Streamyfin/Android TV/Swiftfin. Open: Seerr version (tested 3.5.0 here), disk budget for on-demand movies.

## Code state (src/Jellyfin.Plugin.OnDemand)
- Plugin.cs / PluginConfiguration.cs / Configuration/config.html: settings page (Seerr, qBit, Radarr URLs+keys, webhook token, gate thresholds).
- Seerr/SeerrClient.cs: API key + `X-API-User` impersonation; find user by jellyfinUserId; search; request movie; `SeerrPermissions.AutoApprovesMovies`.
- State/: per-TMDB-id JSON state store (`<data>/ondemand/state.json`).
- Streaming/: PieceMap, BufferGate (header+tail pieces, >=5% done, speed >= 1.5x avg bitrate), QbitClient, GatedFileReader.
- Api/OnDemandController.cs: `/OnDemand/{Search,Me,Play/{tmdb},Status/{tmdb},Stream/{tmdb},Webhook/Radarr}`. User comes from the Jellyfin auth token only.
- Tests: `dotnet test` (9 pass: piece ranges, gate, auto-approve). `dev/smoke.sh` loads the plugin in Jellyfin 10.11.11 (Docker).

## Verified in the cloud env
- Plugin loads in real Jellyfin 10.11.11; unauthenticated endpoints return 401; webhook with bad token 401.
- Seerr 3.5.0 (ghcr.io/seerr-team/seerr): Option A works (API key can list users; `X-API-User` impersonation 200).
- Plugin end to end: Jellyfin token -> `/OnDemand/Me` {seerrUserId, canPlayNow} -> `/OnDemand/Search` returns real Seerr/TMDB results.
- Abyss loads via `@import url('https://cdn.jsdelivr.net/gh/AumGupta/abyss-jellyfin@main/abyss.css');` in Dashboard > Branding; Jellyfin's Download button is in the movie page's ⋮ menu.

## NOT verified / not built
- Never run against live Radarr or qBittorrent: Play, Stream (Range + piece gating), Grab/Import webhooks, `.strm` stub, Radarr import with `.strm` present, watch-progress surviving the stub->real swap.
- Radarr qBittorrent client needs sequential + first/last-piece priority enabled (not done).
- Hard-coded: stub URL `http://localhost:8096/...`; runtime = 2h in `EstimatedDuration`; Stream always `video/mp4`; Stream only starts after hash known (Grab webhook).
- Library scans must not start torrents (Phase 2 check); transcode case -> "Preparing full download" (not built).
- Missing phases: 3 handover (badge "Downloading 43%", download button only after import), 4 UI (`styles/abyss-ondemand.css`, Preparing overlay, Play now on cards), 5 resilience (dead swarm 45s blocklist retry, retention, quotas, logs), 6 native-app virtual search items + delayed playback-info (test Streamyfin/Android TV/Swiftfin timeouts).
- Jellyfin Enhanced + File Transformation not installed in the test env (GitHub plugin download reachability unchecked).

## Rebuilding the cloud test env (container is ephemeral)
1. .NET 9: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0 --install-dir /opt/dotnet`; `export PATH=$PATH:/opt/dotnet`.
2. Docker: `dockerd &` (not auto-started); images pull fine (Docker Hub, ghcr).
3. Jellyfin: `dev/smoke.sh`. Finish setup via API: POST /Startup/Configuration, /Startup/User {admin/adminpass}, /Startup/RemoteAccess, /Startup/Complete; auth with POST /Users/AuthenticateByName and header `Authorization: MediaBrowser Client="t", Device="t", DeviceId="t", Version="1"`.
4. Seerr: `docker run -d --name seerr --network host -v /tmp/seerr-cfg:/app/config -v /root/.ccr/ca-bundle.crt:/ca.crt:ro -e NODE_EXTRA_CA_CERTS=/ca.crt ghcr.io/seerr-team/seerr:latest`.
   Setup: POST /api/v1/auth/jellyfin {username,password,hostname:"172.17.0.2",port:8096,useSsl:false,email,serverType:2} then POST /api/v1/settings/initialize.
   TMDB is only reachable through the session egress proxy: POST /api/v1/settings/network with proxy {enabled:true,hostname:"127.0.0.1",port:45813,...} (axios inside Seerr ignores HTTPS_PROXY env; port from $HTTPS_PROXY). Restart Seerr after.
   Seerr API key: GET /api/v1/settings/main (needs session cookie from the login above).
5. Point plugin at Seerr from the Jellyfin container via the docker bridge: `http://172.17.0.1:5055` (not localhost). POST /Plugins/7d2f6c1e-3b8a-4c55-9a1e-0d6f4b2a9e31/Configuration.
6. Screenshots: `npm i playwright-core`, use /opt/pw-browsers/chromium-1194/chrome-linux/chrome with `proxy:{server:$HTTPS_PROXY,bypass:'localhost,127.0.0.1'}` and `ignoreHTTPSErrors:true` (needed for Abyss CSS/fonts).
7. Test media: ffmpeg in the Jellyfin image at /usr/lib/jellyfin-ffmpeg/ffmpeg; make a synthetic mp4 under /media/Movies/<Name (Year)>/.

## Suggested next steps
1. Add docker-compose (Jellyfin, Seerr, qBittorrent, Radarr pre-linked) and a local test tracker/torrent (public trackers/indexers may be blocked by egress policy) to prove Phase 0/2: partial torrent plays, truncated-file tests, seek within downloaded part.
2. Phase 3 handover, then Phase 4 UI; try installing Jellyfin Enhanced as the baseline.
3. Ask the user: Seerr version confirmed? disk budget?
