#!/usr/bin/env bash
set -euo pipefail
mode="${1:?Indica el modo de prueba}"
godot="${2:?Indica el ejecutable Godot}"
shift 2
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
log="$mode-test.log"
seconds=30
if [ "$mode" = combinations-smoke ]; then seconds=50; fi
marker="${mode^^}"
marker="${marker//-/_}_OK"
timeout "${seconds}s" "$godot" --headless --audio-driver Dummy --path src/Arcana.Client "$@" -- "--$mode" 2>&1 | tee "$log"
grep -q "^$marker:" "$log"
if grep -E '^(ERROR:|SCRIPT ERROR:)|SMOKE_FAILED' "$log"; then
    echo "La prueba registró errores aunque aparezca un marcador de éxito." >&2
    exit 1
fi
