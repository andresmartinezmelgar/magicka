#!/usr/bin/env bash
# Construye un AAB de Play sin el instalador de GitHub y sin acceso a claves de firma.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
godot=$(realpath "${1:?Indica el ejecutable de Godot .NET}")
output=$(realpath -m "${2:?Indica la ruta del AAB sin firmar}")
sdk=${ANDROID_HOME:?Falta Android SDK}
jdk=${JAVA_HOME:?Falta JDK}
test -f "$sdk/platforms/android-36/android.jar"
templates="${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/4.5.2.stable.mono"
test -s "$templates/android_source.zip"
stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
mkdir -p "$stage/src" "$(dirname "$output")"
git -C "$root" archive HEAD | tar -x -C "$stage/src"
cd "$stage/src"
source scripts/tool-support.sh
export XDG_DATA_HOME="$stage/data" XDG_CONFIG_HOME="$stage/config"
python3 - "$templates" "$XDG_DATA_HOME" "$XDG_CONFIG_HOME" "$jdk" "$sdk" <<'PY'
import json, re, shutil, sys, xml.etree.ElementTree as ET, zipfile
from pathlib import Path
templates, data, config, jdk, sdk = map(Path, sys.argv[1:])
config = config / 'godot'
config.mkdir(parents=True)
(config / 'editor_settings-4.5.tres').write_text(
    '[gd_resource type="EditorSettings" format=3]\n\n[resource]\n'
    + 'export/android/java_sdk_path=' + json.dumps(str(jdk)) + '\n'
    + 'export/android/android_sdk_path=' + json.dumps(str(sdk)) + '\n')
local = data / 'godot/export_templates' / templates.name
local.mkdir(parents=True)
for name in ['android_debug.apk', 'android_release.apk', 'version.txt']:
    (local / name).symlink_to(templates / name)
with zipfile.ZipFile(templates / 'android_source.zip') as original, zipfile.ZipFile(local / 'android_source.zip', 'w') as target:
    for entry in original.infolist():
        if entry.filename != 'config.gradle':
            with original.open(entry) as src, target.open(entry, 'w') as dst:
                shutil.copyfileobj(src, dst)
            continue
        text = original.read(entry).decode()
        for key, old, new in [('compileSdk', '35', '36'), ('androidGradlePlugin', "'8.6.1'", "'8.9.2'")]:
            text, count = re.subn(r'(?m)^(\s*' + key + r'\s*:\s*)' + re.escape(old) + r',',
                                  lambda m: m.group(1) + new + ',', text)
            if count != 1: raise ValueError('Plantilla Android inesperada: ' + key)
        target.writestr(entry, text)
preset = Path('src/Arcana.Client/export_presets.cfg')
desktop, mobile = preset.read_text().split('[preset.1]', 1)
for old, new in [('custom_features=""','custom_features="arcana_play"'),
                 ('gradle_build/export_format=0','gradle_build/export_format=1'),
                 ('gradle_build/target_sdk="35"','gradle_build/target_sdk="36"'),
                 ('package/signed=true','package/signed=false')]:
    if mobile.count(old) != 1: raise ValueError('Preset Android inesperado: ' + old)
    mobile = mobile.replace(old,new)
preset.write_text(desktop + '[preset.1]' + mobile)
plugin = Path('android/firebase-plugin/plugin/src/main')
manifest = plugin / 'AndroidManifest.xml'
ns = 'http://schemas.android.com/apk/res/android'
ET.register_namespace('android', ns)
doc = ET.parse(manifest)
excluded = {'android.permission.REQUEST_INSTALL_PACKAGES','android.permission.UPDATE_PACKAGES_WITHOUT_USER_ACTION',
            'org.godotengine.plugin.v2.ArcanaUpdates','com.arcana.firebase.ArcanaUpdateResultActivity'}
for parent in doc.getroot().iter():
    for child in list(parent):
        name = child.get('{' + ns + '}name')
        if name in excluded:
            parent.remove(child)
            excluded.remove(name)
if excluded: raise ValueError('Manifiesto del instalador inesperado')
doc.write(manifest, encoding='utf-8', xml_declaration=True)
classes = plugin / 'java/com/arcana/firebase'
for name in ['ArcanaUpdatesPlugin','ArcanaUpdateResultActivity','UpdateVersionPolicy','UpdateForegroundGate','UpdateRetryPolicy']:
    (classes / (name + '.java')).unlink()
PY
gradle -p android/firebase-plugin :plugin:assembleRelease --no-daemon
cp android/firebase-plugin/plugin/build/outputs/aar/plugin-release.aar src/Arcana.Client/addons/arcana_firebase/ArcanaFirebase.aar
dotnet build src/Arcana.Client/Arcana.Client.csproj -c Debug --nologo
invoke_godot "$stage/import.log" "$godot" --headless --editor --path src/Arcana.Client --import
invoke_godot "$stage/export.log" "$godot" --headless --path src/Arcana.Client --install-android-build-template --export-release Android "$stage/play.aab"
test -s "$stage/play.aab"
mv "$stage/play.aab" "$output"
