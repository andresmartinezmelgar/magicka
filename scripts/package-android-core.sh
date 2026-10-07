#!/usr/bin/env bash
set -euo pipefail
godot="${1:?Indica el ejecutable de Godot 4.5.2 .NET}"
architecture="${2:-arm64}"
case "$architecture" in arm64|x86_64) ;; *) echo 'Arquitectura Android no válida.' >&2; exit 1;; esac
android_sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
java_sdk="${JAVA_HOME:?JAVA_HOME debe señalar a OpenJDK 17}"
test -d "$android_sdk" || { echo 'Falta el SDK Android.' >&2; exit 1; }
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
source "$root/scripts/tool-support.sh"
build_version=$(python3 "$root/scripts/version-support.py" info "$root")
read -r version version_code <<< "$build_version"
output="$root/artifacts/android/ArcanaArena-v$version-Android-$architecture.apk"
mkdir -p "$root/artifacts/android"
temp=$(mktemp -d "$root/artifacts/android/.build-XXXXXXXX")
staged_apk="$temp/$(basename "$output")"
cp src/Arcana.Client/export_presets.cfg "$temp/export_presets.cfg"
trap 'cp "$temp/export_presets.cfg" "$root/src/Arcana.Client/export_presets.cfg"; rm -rf "$temp"' EXIT
python3 "$root/scripts/version-support.py" sync "$root"
if [ "$architecture" = x86_64 ]; then
  python3 - <<'PY'
from pathlib import Path
p=Path('src/Arcana.Client/export_presets.cfg')
s=p.read_text().replace('architectures/arm64-v8a=true','architectures/arm64-v8a=false').replace('architectures/x86_64=false','architectures/x86_64=true')
p.write_text(s)
PY
fi
export XDG_CONFIG_HOME="$temp/config"
export GODOT_ANDROID_KEYSTORE_DEBUG_PATH="$temp/debug.keystore"
export GODOT_ANDROID_KEYSTORE_DEBUG_USER=androiddebugkey
export GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD=android
export GODOT_SILENCE_ROOT_WARNING=1
"$java_sdk/bin/keytool" -genkeypair -noprompt -keystore "$temp/debug.keystore" -storepass android -keypass android \
  -alias androiddebugkey -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=Arcana Arena Development' >/dev/null 2>&1
python3 - "$XDG_CONFIG_HOME" "$java_sdk" "$android_sdk" <<'PY'
import json,sys
from pathlib import Path
p=Path(sys.argv[1])/'godot'; p.mkdir(parents=True,exist_ok=True)
(p/'editor_settings-4.5.tres').write_text('[gd_resource type="EditorSettings" format=3]\n\n[resource]\n'
    +'export/android/java_sdk_path='+json.dumps(sys.argv[2])+'\n'
    +'export/android/android_sdk_path='+json.dumps(sys.argv[3])+'\n')
PY
invoke_godot "$root/artifacts/android/import.log" "$godot" --headless --editor --path src/Arcana.Client --import
invoke_godot "$root/artifacts/android/export.log" "$godot" --headless --path src/Arcana.Client --install-android-build-template --export-debug Android "$staged_apk"
"$android_sdk/build-tools/35.0.0/apksigner" verify --verbose "$staged_apk"
"$android_sdk/build-tools/35.0.0/aapt" dump badging "$staged_apk" > "$temp/manifest.txt"
python3 - "$staged_apk" "$version" "$temp/manifest.txt" "$architecture" "$version_code" <<'PY'
import re,sys,zipfile
from pathlib import Path
apk,version,manifest,arch,code=sys.argv[1:]
abi='arm64-v8a' if arch=='arm64' else 'x86_64'
text=Path(manifest).read_text()
assert "package: name='com.arcana.arena'" in text
actual_name=re.search(r"versionName='([^']*)'", text)
actual_code=re.search(r"versionCode='([0-9]+)'", text)
assert actual_name and actual_name[1]==version, f'Version APK incorrecta: esperada {version}, encontrada {actual_name[1] if actual_name else "ausente"}.'
assert actual_code and actual_code[1]==code, f'Codigo APK incorrecto: esperado {code}, encontrado {actual_code[1] if actual_code else "ausente"}.'
assert "sdkVersion:'26'" in text and "targetSdkVersion:'35'" in text
assert "android.permission.INTERNET" in text
with zipfile.ZipFile(apk) as z:
    names=z.namelist()
    assert f'lib/{abi}/libgodot_android.so' in names
    assert any(f'lib/{abi}/' in n and 'mono' in n for n in names), 'Falta el runtime C# Android'
    assert all(n.split('/')[1]==abi for n in names if n.startswith('lib/') and n.endswith('.so'))
print(f'ANDROID_PACKAGE_OK: firma, identidad, versión, Android 8+, red, Godot y C# {arch}.')
PY
hash_file=SHA256SUMS-Android.txt
if [ "$architecture" = x86_64 ]; then hash_file=SHA256SUMS-Android-x86_64.txt; fi
(cd "$temp" && sha256sum "$(basename "$output")" > "$hash_file")
# La salida temporal está en el mismo sistema de archivos. Solo sustituye una APK ya verificada.
mv -f "$staged_apk" "$output"
mv -f "$temp/$hash_file" "$root/artifacts/android/$hash_file"
mv -f "$temp/manifest.txt" "$root/artifacts/android/manifest.txt"
printf 'APK: %s\n' "$output"
