#!/usr/bin/env bash
set -euo pipefail
godot="${1:?Indica el ejecutable Godot .NET}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
mkdir -p artifacts/render-profile
export LIBGL_ALWAYS_SOFTWARE=1 GODOT_SILENCE_ROOT_WARNING=1
for map in Classic Citadel Ascension Relics; do
  log="$root/artifacts/render-profile/$map.log"
  output="$root/artifacts/render-profile/$map.json"
  timeout 120s xvfb-run -a "$godot" --audio-driver Dummy --resolution 960x540 --path src/Arcana.Client -- \
    --profile-render --profile-map="$map" --profile-warmup=1 --profile-seconds=3 \
    --profile-environment=ci-linux-software-opengl --profile-output="$output" 2>&1 | tee "$log"
  test -s "$output"
  if grep -nE '^(ERROR:|SCRIPT ERROR:)|PROFILE_FAILED' "$log"; then exit 1; fi
  python3 scripts/check-render-profile.py "$output"
done
