#!/usr/bin/env bash
set -euo pipefail
godot="${1:?Indica el ejecutable Godot}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
atlas=src/Arcana.Client/Assets/Wizards/atlases.json
backup="$atlas.smoke-backup"
test ! -e "$backup"
mv "$atlas" "$backup"
trap 'mv "$backup" "$atlas"' EXIT
set +e
timeout 30s "$godot" --headless --audio-driver Dummy --path src/Arcana.Client -- --smoke > smoke-rejection-test.log 2>&1
status=$?
set -e
if [ "$status" -ne 1 ] || grep -q '^SMOKE_OK:' smoke-rejection-test.log || ! grep -q 'SMOKE_FAILED:' smoke-rejection-test.log; then
    cat smoke-rejection-test.log
    echo "El cliente no rechazó correctamente un fallo real de carga." >&2
    exit 1
fi
echo "SMOKE_REJECTION_OK: un atlas ausente produce salida 1 sin declarar éxito."
