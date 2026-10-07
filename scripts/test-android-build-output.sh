#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$root/scripts/tool-support.sh"
test_root=$(mktemp -d)
trap 'rm -rf "$test_root"' EXIT
project="$test_root/proyecto con espacios"
mkdir -p "$project/scripts" "$project/src/Arcana.Client" "$project/artifacts/android" \
  "$test_root/java/bin" "$test_root/sdk/build-tools/35.0.0"
cp "$root/scripts/package-android.sh" "$root/scripts/package-android-core.sh" "$root/scripts/build-lock.py" "$root/scripts/tool-support.sh" "$root/scripts/version-support.py" "$project/scripts/"
cp "$root/VERSION" "$project/"
cp "$root/src/Arcana.Client/export_presets.cfg" "$project/src/Arcana.Client/"
export JAVA_HOME="$test_root/java" ANDROID_HOME="$test_root/sdk"
cat > "$JAVA_HOME/bin/keytool" <<'SH'
#!/usr/bin/env bash
exit 0
SH
cat > "$ANDROID_HOME/build-tools/35.0.0/apksigner" <<'SH'
#!/usr/bin/env bash
if [ "${FAKE_FAILURE:-}" = signature ]; then exit 1; fi
SH
cat > "$ANDROID_HOME/build-tools/35.0.0/aapt" <<'SH'
#!/usr/bin/env bash
python3 - "${*: -1}" <<'PY'
import sys,zipfile
with zipfile.ZipFile(sys.argv[1]) as apk:
    print(apk.read('manifest-test.txt').decode())
PY
SH
cat > "$test_root/godot" <<'SH'
#!/usr/bin/env bash
if [ "${*: -1}" = --import ]; then
  if [ "${FAKE_FAILURE:-}" = import ]; then echo 'SCRIPT ERROR: No se pudo importar.'; fi
  exit 0
fi
python3 - "${*: -1}" <<'PY'
import sys
import zipfile
import os,re
from pathlib import Path
presets=Path('src/Arcana.Client/export_presets.cfg').read_text()
version=re.search(r'^version/name="([^"]+)"', presets, re.M)[1]
code=re.search(r'^version/code=(\d+)', presets, re.M)[1]
if os.environ.get('FAKE_FAILURE')=='version': version='0.0.1'
if os.environ.get('FAKE_FAILURE')=='code': code='1'
with zipfile.ZipFile(sys.argv[1], 'w') as apk:
    abi='x86_64' if 'architectures/x86_64=true' in presets else 'arm64-v8a'
    apk.writestr(f'lib/{abi}/libgodot_android.so', 'Godot de prueba')
    apk.writestr(f'lib/{abi}/libmonosgen-2.0.so', 'Mono de prueba')
    apk.writestr('manifest-test.txt', f"package: name='com.arcana.arena' versionCode='{code}' versionName='{version}'\nsdkVersion:'26'\ntargetSdkVersion:'35'\nandroid.permission.INTERNET\n")
PY
if [ "${FAKE_FAILURE:-}" = export ]; then echo 'ERROR: No se pudo exportar.'; exit 0; fi
if [ "${FAKE_FAILURE:-}" = exit ]; then exit 1; fi
echo 'Exportación completada.'
echo 'ERROR: Condition "!EditorSettings::get_singleton() || !EditorSettings::get_singleton()->has_setting(p_setting)" is true. Returning: Variant()'
echo '   at: _EDITOR_GET (editor/settings/editor_settings.cpp:1514)'
SH
chmod +x "$test_root/godot" "$JAVA_HOME/bin/keytool" "$ANDROID_HOME/build-tools/35.0.0/"*
version=$(tr -d '\r\n' < "$root/VERSION")
output="$project/artifacts/android/ArcanaArena-v$version-Android-arm64.apk"
presets="$project/src/Arcana.Client/export_presets.cfg"
preset_hash=$(sha256sum "$presets")
printf 'APK anterior' > "$output"
bash "$project/scripts/package-android.sh" "$test_root/godot" > "$test_root/success.log" 2>&1 || { cat "$test_root/success.log"; exit 1; }
python3 - "$output" <<'PY'
import sys
import zipfile
assert zipfile.is_zipfile(sys.argv[1]), 'La compilación correcta no sustituyó la APK.'
PY
test "$(sha256sum "$presets")" = "$preset_hash"
(cd "$project/artifacts/android" && sha256sum -c SHA256SUMS-Android.txt)
for failure in import export exit signature version code; do
  printf 'APK anterior que debe conservarse' > "$output"
  apk_hash=$(sha256sum "$output")
  checksum_hash=$(sha256sum "$project/artifacts/android/SHA256SUMS-Android.txt")
  if FAKE_FAILURE="$failure" bash "$project/scripts/package-android.sh" "$test_root/godot" > "$test_root/$failure.log" 2>&1; then
    cat "$test_root/$failure.log"; echo "No se rechazó el fallo: $failure" >&2; exit 1
  fi
  test "$(sha256sum "$output")" = "$apk_hash"
  test "$(sha256sum "$presets")" = "$preset_hash"
  test "$(sha256sum "$project/artifacts/android/SHA256SUMS-Android.txt")" = "$checksum_hash"
done
# Cambiar solo VERSION tambien actualiza el manifest; el preset original se restaura.
printf '0.13.0\n' > "$project/VERSION"
future_output="$project/artifacts/android/ArcanaArena-v0.13.0-Android-x86_64.apk"
bash "$project/scripts/package-android.sh" "$test_root/godot" x86_64 > "$test_root/future.log" 2>&1 || { cat "$test_root/future.log"; exit 1; }
python3 - "$future_output" <<'PY'
import sys,zipfile
with zipfile.ZipFile(sys.argv[1]) as apk:
    manifest=apk.read('manifest-test.txt').decode()
    assert "versionName='0.13.0'" in manifest and "versionCode='13000'" in manifest
    assert 'lib/x86_64/libgodot_android.so' in apk.namelist()
PY
test "$(sha256sum "$presets")" = "$preset_hash"
test -z "$(find "$project/artifacts/android" -maxdepth 1 -name '.build-*' -print -quit)"
cat > "$test_root/error-tool" <<'SH'
#!/usr/bin/env bash
echo 'ERROR: Condition "!EditorSettings::get_singleton() || !EditorSettings::get_singleton()->has_setting(p_setting)" is true. Returning: Variant()'
SH
chmod +x "$test_root/error-tool"
if invoke_godot "$test_root/unpaired.log" "$test_root/error-tool" > /dev/null 2>&1; then
  echo 'Se aceptó el mensaje de EditorSettings sin la traza concreta.' >&2; exit 1
fi
echo 'ANDROID_BASH_BUILD_OK: sustitución, VERSION futura, ambas arquitecturas, rechazo de versión/código incorrectos, errores de Godot, firma, APK anterior, checksum, presets y limpieza.'
