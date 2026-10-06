#!/usr/bin/env bash
# Godot puede devolver código cero después de registrar errores de importación o exportación.
invoke_godot() {
  local log="${1:?Indica el registro de Godot}"
  shift
  local status=0
  "$@" 2>&1 | tee "$log" || status=$?
  python3 - "$log" "$status" <<'PY'
import re
import sys
from pathlib import Path

lines = Path(sys.argv[1]).read_text(errors='replace').splitlines()
status = int(sys.argv[2])
if status:
    sys.exit(f'Godot terminó con código {status}.')
warning = 'ERROR: Condition "!EditorSettings::get_singleton() || !EditorSettings::get_singleton()->has_setting(p_setting)" is true. Returning: Variant()'
for index, line in enumerate(lines):
    line = line.strip()
    if line == warning and index + 1 < len(lines) and re.fullmatch(
        r'\s*at: _EDITOR_GET \(editor/(settings/)?editor_settings\.cpp:\d+\)\s*', lines[index + 1]):
        continue
    if re.search(r'^(ERROR:|SCRIPT ERROR:)|Failed to build project|Export failed', line):
        sys.exit('Godot registró un error: ' + line)
PY
}
