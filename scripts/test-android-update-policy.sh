#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
classes="$(mktemp -d)"
trap 'rm -rf "$classes"' EXIT
javac --release 17 -encoding UTF-8 -d "$classes" \
    "$root/android/firebase-plugin/plugin/src/main/java/com/arcana/firebase/UpdateVersionPolicy.java" \
    "$root/tests/android-update-policy/UpdateVersionPolicyTest.java"
java -cp "$classes" com.arcana.firebase.UpdateVersionPolicyTest
