#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
arch="${ARCANA_DOCKER_ARCH:-amd64}"
name="arcana-docker-test-$arch-$$"
image="$name:local"
volume="$name-data"
cleanup() { docker rm -f "$name" >/dev/null 2>&1 || true; docker volume rm "$volume" >/dev/null 2>&1 || true; }
trap cleanup EXIT
docker buildx build --platform "linux/$arch" --load --build-arg "ARCANA_VERSION=$(tr -d '\r\n' < VERSION)" -t "$image" .
docker volume create "$volume" >/dev/null
docker run -d --name "$name" --platform "linux/$arch" -p 127.0.0.1::27015 -v "$volume:/app/data" "$image" >/dev/null
port=$(docker port "$name" 27015/tcp | sed 's/.*://')
ready=false
for i in {1..120}; do
  if curl -fsS "http://127.0.0.1:$port/health" >/dev/null; then ready=true; break; fi
  sleep 0.5
done
if [ "$ready" != true ]; then docker logs "$name"; exit 1; fi
docker exec "$name" sh -c 'test "$(id -u)" != 0 && printf durable > /app/data/rewards/probe.tmp && mv /app/data/rewards/probe.tmp /app/data/rewards/probe'
docker rm -f "$name" >/dev/null
docker run --rm --platform "linux/$arch" --entrypoint sh -v "$volume:/app/data" "$image" -c 'test "$(cat /app/data/rewards/probe)" = durable'
echo "DOCKER_SERVER_OK: $arch, HTTP, usuario sin privilegios y datos conservados al recrear el contenedor."
