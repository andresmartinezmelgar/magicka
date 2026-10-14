#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
export GAME_PORT=29051
export DISCOVERY_PORT=29052
export SERVER_PASSWORD=serie-pvp-ci
export TEST_PASSWORD="$SERVER_PASSWORD"
export BOTS=0
unset ARCANA_CI_LOOPBACK_GUESTS
dotnet src/Arcana.Server/bin/Release/net8.0/Arcana.Server.dll > "$RUNNER_TEMP/series-server.log" 2>&1 &
pid=$!
trap 'kill "$pid" 2>/dev/null || true' EXIT
ready=false
for i in {1..75}; do
  if curl -fsS "http://127.0.0.1:$GAME_PORT/health" >/dev/null 2>&1; then ready=true; break; fi
  sleep 0.2
done
if [ "$ready" != true ]; then cat "$RUNNER_TEMP/series-server.log"; exit 1; fi
dotnet tests/Arcana.Tests/bin/Release/net8.0/Arcana.Tests.dll --server="127.0.0.1:$GAME_PORT" --series-lobby-only
