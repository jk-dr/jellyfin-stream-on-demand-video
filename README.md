# Jellyfin On-Demand Plugin

Press play on any Seerr movie and watch while it downloads. See the plan docx for the design.
Targets Jellyfin 10.11.11 (.NET 9).

## Status
- Phase 1 skeleton: config page, Seerr client (API key + X-API-User), state store, auth'd endpoints.
- Phase 2 pieces: piece map, buffer gate (5% / 1.5x bitrate / head+tail), gated Range stream endpoint, qBittorrent client.
- Radarr Grab/Import webhook creates/removes the `.strm` stub.
- NOT yet verified against live Seerr/Radarr/qBittorrent (Phase 0 spikes), no UI (Phase 4), no native-app search (Phase 6).

## Dev
    dotnet test              # gate/piece unit tests
    dev/smoke.sh             # loads the plugin into Jellyfin 10.11.11 in Docker
