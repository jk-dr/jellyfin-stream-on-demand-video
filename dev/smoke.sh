#!/usr/bin/env bash
# Build the plugin and load it into a real Jellyfin 10.11.11 container; checks auth is enforced.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet publish src/Jellyfin.Plugin.OnDemand -c Release -o /tmp/ondemand-pub >/dev/null
P=/tmp/jf/config/plugins/OnDemand_0.1.0.0; rm -rf /tmp/jf; mkdir -p "$P" /tmp/jf/cache
cp /tmp/ondemand-pub/Jellyfin.Plugin.OnDemand.dll "$P"/
docker rm -f jf >/dev/null 2>&1 || true
docker run -d --name jf -p 8096:8096 -v /tmp/jf/config:/config -v /tmp/jf/cache:/cache jellyfin/jellyfin:10.11.11 >/dev/null
for _ in $(seq 40); do curl -s localhost:8096/System/Info/Public | grep -q Version && break; sleep 3; done
docker logs jf 2>&1 | grep -q "Loaded plugin: On-Demand Streaming" && echo "plugin loaded"
[ "$(curl -s -o /dev/null -w '%{http_code}' 'localhost:8096/OnDemand/Search?q=x')" = 401 ] && echo "auth enforced"
