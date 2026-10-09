#!/usr/bin/env bash
set -euo pipefail
godot="${1:?Indica el ejecutable Godot}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
mkdir -p artifacts/loading-review
export GODOT_SILENCE_ROOT_WARNING=1 LIBGL_ALWAYS_SOFTWARE=1
GAME_PORT=29035 DISCOVERY_PORT=29036 SERVER_PASSWORD='' dotnet src/Arcana.Server/bin/Release/net8.0/Arcana.Server.dll > artifacts/loading-review/server.log 2>&1 &
server_pid=$!
cleanup() { kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true; }
trap cleanup EXIT
ready=false
for attempt in {1..100}; do
    if curl -fsS http://127.0.0.1:29035/health > /dev/null 2>&1; then ready=true; break; fi
    sleep 0.1
done
if [ "$ready" != true ]; then cat artifacts/loading-review/server.log; exit 1; fi
run_case() {
    local name="$1" marker="$2"
    shift 2
    local log="artifacts/loading-review/$name.log"
    timeout 60s xvfb-run -a "$godot" --audio-driver Dummy --path src/Arcana.Client -- "$@" 2>&1 | tee "$log"
    grep -F "$marker" "$log"
    if grep -E '^(ERROR:|SCRIPT ERROR:)|SMOKE_FAILED' "$log"; then exit 1; fi
}
run_case desktop LOADING_SMOKE_OK: --loading-smoke
run_case mobile LOADING_SMOKE_OK: --loading-smoke --mobile
run_case room LOBBY_SMOKE_OK: --lobby-smoke=127.0.0.1:29035 --mobile
run_case loading-preview 'CAPTURE: Ok' --preview-loading --mobile --capture="$root/artifacts/loading-review/loading.png"
run_case room-preview 'CAPTURE: Ok' --preview-rooms=127.0.0.1:29035 --mobile --capture="$root/artifacts/loading-review/room.png"
test -s artifacts/loading-review/loading.png
test -s artifacts/loading-review/room.png
