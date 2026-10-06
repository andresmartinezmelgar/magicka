#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Protege presets, herramientas y salida antes de iniciar cualquier fase.
exec python3 "$root/scripts/build-lock.py" "$root" bash "$root/scripts/package-android-core.sh" "$@"
