#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
version=$(tr -d '\r\n' < VERSION)
android_sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:?Falta el SDK Android}}"
apk="artifacts/android/ArcanaArena-v$version-Android-x86_64.apk"
mkdir -p artifacts/android
test -e /dev/kvm
if [ ! -r /dev/kvm ] || [ ! -w /dev/kvm ]; then sudo chmod 666 /dev/kvm; fi
echo no | "$android_sdk/cmdline-tools/latest/bin/avdmanager" create avd --force --name arcana --package 'system-images;android-35;google_apis;x86_64' --device pixel_6
"$android_sdk/emulator/emulator" -avd arcana -no-window -no-audio -no-boot-anim -no-snapshot -gpu swiftshader_indirect -accel on -memory 2048 > artifacts/android/emulator.log 2>&1 &
emulator_pid=$!
trap 'adb emu kill >/dev/null 2>&1 || true; kill "$emulator_pid" 2>/dev/null || true' EXIT
timeout 180 adb wait-for-device
booted=false
for i in {1..90}; do
  if [ "$(adb shell getprop sys.boot_completed | tr -d '\r')" = 1 ]; then booted=true; break; fi
  sleep 2
done
$booted || { cat artifacts/android/emulator.log; exit 1; }
adb shell input keyevent 82
adb install -r "$apk"
adb logcat -c
adb shell am start -n com.arcana.arena/com.godot.game.GodotApp --esa command_line_params '--,--mobile-smoke'
passed=false
for i in {1..90}; do
  adb logcat -d > artifacts/android/device.log
  if rg 'MOBILE_SMOKE_OK:' artifacts/android/device.log; then passed=true; break; fi
  if rg 'MOBILE_SMOKE_FAILED|FATAL EXCEPTION|Unhandled exception|SMOKE_FAILED' artifacts/android/device.log; then break; fi
  sleep 2
done
if ! $passed; then cat artifacts/android/device.log; exit 1; fi
if rg 'MOBILE_SMOKE_FAILED|FATAL EXCEPTION|Unhandled exception|SMOKE_FAILED' artifacts/android/device.log; then exit 1; fi
echo 'ANDROID_DEVICE_OK: APK instalada; C#, Godot, controles y práctica ejecutados en Android 15.'
