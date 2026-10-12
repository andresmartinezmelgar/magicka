#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
emulator_pid=""
cleanup() {
    if [ -n "$emulator_pid" ]; then kill "$emulator_pid" 2>/dev/null || true; wait "$emulator_pid" 2>/dev/null || true; fi
}
trap cleanup EXIT
if [ -z "${FIRESTORE_EMULATOR_HOST:-}" ]; then
    tools="$root/.tools/test-firestore"
    mkdir -p "$tools"
    jar="$tools/cloud-firestore-emulator-v1.19.8.jar"
    if [ ! -f "$jar" ]; then
        curl -fL --retry 2 https://storage.googleapis.com/firebase-preview-drop/emulator/cloud-firestore-emulator-v1.19.8.jar -o "$jar.tmp"
        mv "$jar.tmp" "$jar"
    fi
    python3 - "$jar" <<'PY'
import hashlib,sys
if hashlib.sha256(open(sys.argv[1],'rb').read()).hexdigest() != '9d43599ed6151199e8d604dc87fac51218e49e5f3a48519b1ae560bbe5e3382d':
    raise SystemExit('El emulador Firestore no coincide con la versión fijada.')
PY
    java -jar "$jar" --host=127.0.0.1 --port=28338 --project_id=demo-arcana-accounts --rules=firebase/firestore.rules > "$tools/emulator.log" 2>&1 &
    emulator_pid=$!
    export FIRESTORE_EMULATOR_HOST=127.0.0.1:28338
    python3 - <<'PY'
import socket,time
for _ in range(100):
    try:
        with socket.create_connection(('127.0.0.1',28338),timeout=.2): break
    except OSError: time.sleep(.2)
else: raise SystemExit('El emulador Firestore no respondió. Consulta .tools/test-firestore/emulator.log.')
PY
fi
dotnet run --project tests/Arcana.Account.Tests -c Release --no-build
