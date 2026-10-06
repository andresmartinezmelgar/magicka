#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
version=$(tr -d '\r\n' < VERSION)
android_sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:?Falta el SDK Android}}"
apk="artifacts/android/ArcanaArena-v$version-Android-x86_64.apk"
mkdir -p artifacts/android
export ANDROID_USER_HOME="$root/artifacts/android/emulator-user"
export ANDROID_EMULATOR_HOME="$ANDROID_USER_HOME"
export ANDROID_AVD_HOME="$ANDROID_USER_HOME/avd"
mkdir -p "$ANDROID_AVD_HOME"
serial=emulator-5554
test -e /dev/kvm
if [ ! -r /dev/kvm ] || [ ! -w /dev/kvm ]; then sudo chmod 666 /dev/kvm; fi
echo no | "$android_sdk/cmdline-tools/latest/bin/avdmanager" create avd --force --name arcana --path "$ANDROID_AVD_HOME/arcana.avd" --package 'system-images;android-35;google_apis;x86_64' --device pixel_6
test -s "$ANDROID_AVD_HOME/arcana.ini"
python3 - "$ANDROID_AVD_HOME/arcana.avd/config.ini" <<'PY'
from pathlib import Path
import sys
config = Path(sys.argv[1])
settings = {'disk.dataPartition.size': '2G', 'disk.cachePartition.size': '128M',
            'hw.lcd.width': '480', 'hw.lcd.height': '960', 'hw.lcd.density': '160'}
lines = [line for line in config.read_text().splitlines() if line.split('=', 1)[0].strip() not in settings]
config.write_text('\n'.join(lines + [f'{key}={value}' for key, value in settings.items()]) + '\n')
print(config.read_text())
PY
# Liberar el daemon de compilación antes de reservar memoria para el dispositivo.
"$root/src/Arcana.Client/android/build/gradlew" --stop >/dev/null
adb start-server
# ANGLE evita el conteo incorrecto de uniformes de bloques del antiguo SwiftShader GLES.
"$android_sdk/emulator/emulator" -avd arcana -port 5554 -partition-size 2048 -no-metrics -no-window -no-audio -no-boot-anim -no-snapshot -gpu swangle -accel on -memory 2048 > artifacts/android/emulator.log 2>&1 &
emulator_pid=$!
trap 'adb -s "$serial" emu kill >/dev/null 2>&1 || true; kill "$emulator_pid" 2>/dev/null || true' EXIT
booted=false
for i in {1..120}; do
  if ! kill -0 "$emulator_pid" 2>/dev/null; then cat artifacts/android/emulator.log; exit 1; fi
  if [ "$(adb -s "$serial" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r' || true)" = 1 ]; then booted=true; break; fi
  sleep 2
done
$booted || { cat artifacts/android/emulator.log; exit 1; }
adb -s "$serial" shell input keyevent 82
adb -s "$serial" install -r "$apk"
adb -s "$serial" logcat -c
adb -s "$serial" shell am start -n com.arcana.arena/com.godot.game.GodotApp --esa command_line_params '--,--mobile-smoke'
passed=false
for i in {1..90}; do
  adb -s "$serial" logcat -d > artifacts/android/device.log
  if grep -E 'MOBILE_SMOKE_OK:' artifacts/android/device.log; then passed=true; break; fi
  if grep -E 'MOBILE_SMOKE_FAILED|FATAL EXCEPTION|Unhandled exception|SMOKE_FAILED' artifacts/android/device.log; then break; fi
  sleep 2
done
if ! $passed; then cat artifacts/android/device.log; exit 1; fi
if grep -E 'MOBILE_SMOKE_FAILED|FATAL EXCEPTION|Unhandled exception|SMOKE_FAILED' artifacts/android/device.log; then exit 1; fi
adb -s "$serial" shell am force-stop com.arcana.arena
adb -s "$serial" logcat -c
adb -s "$serial" shell am start -n com.arcana.arena/com.godot.game.GodotApp --esa command_line_params '--,--profile-render,--profile-warmup=0,--profile-seconds=3,--profile-environment=ci-android15-emulator-swangle'
profiled=false
for i in {1..120}; do
  adb -s "$serial" logcat -d > artifacts/android/profile-device.log
  if rg -n 'PROFILE_FAILED|FATAL EXCEPTION|Unhandled exception' artifacts/android/profile-device.log; then break; fi
  if rg -n 'PROFILE_OK:' artifacts/android/profile-device.log; then profiled=true; break; fi
  sleep 2
done
$profiled || { cat artifacts/android/profile-device.log; exit 1; }
adb -s "$serial" shell run-as com.arcana.arena cat files/profiles/latest.json > artifacts/android/render-profile.json
python3 scripts/check-render-profile.py artifacts/android/render-profile.json
echo 'ANDROID_DEVICE_OK: APK instalada; C#, Godot, joystick de área, controles, práctica y perfil gráfico ejecutados en Android 15.'
