#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
export GAME_PORT="${GAME_PORT:-27015}"
export DISCOVERY_PORT="${DISCOVERY_PORT:-27016}"
export SERVER_PASSWORD="${SERVER_PASSWORD:-arcana-ci-test}"
export TEST_PASSWORD="$SERVER_PASSWORD"
export BOTS=0
# La prueba debe ejercitar el acceso como invitado SIN el atajo especial de CI.
# Sin Firebase configurado, el servidor local debe aceptar jugadores LAN reales.
unset ARCANA_CI_LOOPBACK_GUESTS
dotnet src/Arcana.Server/bin/Release/net8.0/Arcana.Server.dll > server-test.log 2>&1 &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true' EXIT
ready=false
for i in {1..50}; do
  if curl -fsS "http://127.0.0.1:$GAME_PORT/health" > /dev/null; then ready=true; break; fi
  sleep 0.2
done
if [ "$ready" != true ]; then cat server-test.log; exit 1; fi
dotnet tests/Arcana.Tests/bin/Release/net8.0/Arcana.Tests.dll --server="127.0.0.1:$GAME_PORT" --discovery-port="$DISCOVERY_PORT"
