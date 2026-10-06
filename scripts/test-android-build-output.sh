#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$root/scripts/tool-support.sh"
test_root=$(mktemp -d)
trap 'rm -rf "$test_root"' EXIT
project="$test_root/proyecto con espacios"
mkdir -p "$project/scripts" "$project/src/Arcana.Client" "$project/artifacts/android" \
  "$test_root/java/bin" "$test_root/sdk/build-tools/35.0.0"
cp "$root/scripts/package-android.sh" "$root/scripts/tool-support.sh" "$project/scripts/"
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
version=$(tr -d '\r\n' < VERSION)
printf "package: name='com.arcana.arena' versionName='%s'\nsdkVersion:'26'\ntargetSdkVersion:'35'\nandroid.permission.INTERNET\n" "$version"
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
with zipfile.ZipFile(sys.argv[1], 'w') as apk:
    apk.writestr('lib/arm64-v8a/libgodot_android.so', 'Godot de prueba')
    apk.writestr('lib/arm64-v8a/libmonosgen-2.0.so', 'Mono de prueba')
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
for failure in import export exit signature; do
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
test -z "$(find "$project/artifacts/android" -maxdepth 1 -name '.build-*' -print -quit)"
cat > "$test_root/error-tool" <<'SH'
#!/usr/bin/env bash
echo 'ERROR: Condition "!EditorSettings::get_singleton() || !EditorSettings::get_singleton()->has_setting(p_setting)" is true. Returning: Variant()'
SH
chmod +x "$test_root/error-tool"
if invoke_godot "$test_root/unpaired.log" "$test_root/error-tool" > /dev/null 2>&1; then
  echo 'Se aceptó el mensaje de EditorSettings sin la traza concreta.' >&2; exit 1
fi
echo 'ANDROID_BASH_BUILD_OK: sustitución validada, errores con código cero, fallo de firma, APK anterior, checksum, presets y limpieza.'
