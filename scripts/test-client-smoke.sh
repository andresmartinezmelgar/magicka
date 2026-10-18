#!/usr/bin/env bash
set -euo pipefail
mode="${1:?Indica el modo de prueba}"
godot="${2:?Indica el ejecutable Godot}"
shift 2
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
if [[ "$mode" = equipment-* || "$mode" = shop-smoke || "$mode" = sale-smoke ]]; then
    equipment_cache="$(mktemp -d)"
    export XDG_DATA_HOME="$equipment_cache"
    trap 'rm -rf "$equipment_cache"' EXIT
fi
verify() {
    local check_mode="$1"
    shift
    local log="$check_mode-test.log"
    local seconds=30
    if [ "$check_mode" = combinations-smoke ]; then seconds=50; fi
    if [ "$check_mode" = sale-smoke ]; then seconds=50; fi
    # Mobile smoke covers 160 rendered frames; --max-fps 2 needs at least 80 s
    # plus Godot startup, even when every gameplay assertion succeeds.
    if [ "$check_mode" = mobile-smoke ]; then seconds=120; fi
    local marker="${check_mode^^}"
    marker="${marker//-/_}_OK"
    timeout "${seconds}s" "$godot" --headless --audio-driver Dummy --path src/Arcana.Client "$@" -- "--$check_mode" 2>&1 | tee "$log"
    grep -q "^$marker:" "$log"
    if grep -E '^(ERROR:|SCRIPT ERROR:)|SMOKE_FAILED' "$log"; then
        echo "La prueba registró errores aunque aparezca un marcador de éxito." >&2
        exit 1
    fi
}
verify "$mode" "$@"
if [ "$mode" = equipment-smoke ]; then verify equipment-restore-smoke "$@"; fi
