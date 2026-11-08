#!/usr/bin/env python3
"""Actualiza el servidor Docker desde la última Release estable; Python estándar."""
import argparse
import fcntl
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import urllib.request


REPOSITORY = "andresmartinezmelgar/magicka"
STATE = Path("/var/lib/arcana-server-updater")
CONFIG = Path("/etc/arcana-server-updater.json")
PROGRAM = Path("/usr/local/lib/arcana-server-updater/update.py")
UNITS = Path("/etc/systemd/system")
TAG = re.compile(r"v(\d+)\.(\d+)\.(\d+)")


def log(message):
    print(time.strftime("%Y-%m-%d %H:%M:%S"), message, flush=True)


def write_json(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def command(arguments, capture=False, cwd=None):
    result = subprocess.run(arguments, check=True, text=True, cwd=cwd,
                            stdout=subprocess.PIPE if capture else None,
                            timeout=3600)
    return result.stdout.strip() if capture else ""


def compose_command():
    for candidate in (["docker", "compose"], ["docker-compose"]):
        try:
            subprocess.run([*candidate, "version"], check=True,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return candidate
        except (OSError, subprocess.CalledProcessError):
            pass
    raise RuntimeError("Docker Compose no está instalado o no se puede ejecutar.")


def get_json(url):
    request = urllib.request.Request(url, headers={
        "Accept": "application/json", "User-Agent": "Arcana-Server-Updater"})
    with urllib.request.urlopen(request, timeout=20) as response:
        content = response.read(1024 * 1024 + 1)
    if len(content) > 1024 * 1024:
        raise RuntimeError("Respuesta HTTP demasiado grande.")
    return json.loads(content)


def version(value):
    match = TAG.fullmatch("v" + str(value).removeprefix("v"))
    if not match:
        raise RuntimeError("Versión no reconocida: " + str(value))
    return tuple(map(int, match.groups()))


def stable_tag(release):
    tag = release.get("tag_name", "")
    if release.get("draft") is not False or release.get("prerelease") is not False or not TAG.fullmatch(tag):
        raise RuntimeError("GitHub no ha devuelto una Release estable válida.")
    return tag


class Updater:
    def __init__(self, config, state=STATE):
        self.config = config
        self.state = state
        self.compose = compose_command()
        self.checkout = Path(config["checkout"])
        self.source = state / "source"
        self.override = state / "compose.update.json"

    def base_command(self):
        result = [*self.compose, "--project-directory", str(self.checkout),
                  "--project-name", self.config["project"]]
        for filename in self.config["compose_files"]:
            result += ["-f", filename]
        return result

    def container(self):
        identifier = command([*self.base_command(), "ps", "-q", "server"], capture=True)
        if not identifier:
            return None
        if len(identifier.splitlines()) != 1:
            raise RuntimeError("Se esperaba una única instancia del servidor.")
        return json.loads(command(["docker", "inspect", identifier], capture=True))[0]

    def status(self, container):
        environment = dict(entry.split("=", 1) for entry in container["Config"]["Env"] if "=" in entry)
        port = int(environment.get("GAME_PORT", "27015"))
        if not 1024 <= port <= 65535:
            raise RuntimeError("Puerto del servidor no válido.")
        base = "http://127.0.0.1:" + str(port)
        health = get_json(base + "/health")
        directory = get_json(base + "/rooms")
        if health.get("status") != "ok":
            raise RuntimeError("El servidor actual no está sano; no se sustituirá.")
        # /rooms.server.players incluye la sala principal, las adicionales y los bots.
        players = directory["server"]["players"]
        rooms = directory["rooms"]
        counts = [players, int(environment.get("BOTS", "0"))]
        counts.extend(room["bots"] for room in rooms)
        if any(type(count) is not int or count < 0 for count in counts):
            raise RuntimeError("No se pudo comprobar la ocupación del servidor.")
        if any(type(room.get("players")) is not int or room["players"] < room["bots"] for room in rooms):
            raise RuntimeError("Directorio de salas incoherente; actualización aplazada.")
        humans = players - sum(counts[1:])
        if humans < 0:
            raise RuntimeError("Recuento de jugadores incoherente; actualización aplazada.")
        version(health["version"])
        return health["version"], humans, base

    def fetch_source(self, tag):
        self.source.mkdir(parents=True, exist_ok=True)
        if not (self.source / ".git").exists():
            command(["git", "init", str(self.source)])
            command(["git", "-C", str(self.source), "remote", "add", "origin",
                     "https://github.com/" + REPOSITORY + ".git"])
        command(["git", "-C", str(self.source), "fetch", "--depth=1", "origin",
                 "refs/tags/" + tag + ":refs/tags/" + tag])
        commit = command(["git", "-C", str(self.source), "rev-parse", tag + "^{commit}"], capture=True)
        command(["git", "-C", str(self.source), "checkout", "--detach", "--force", commit])
        if (self.source / "VERSION").read_text(encoding="utf-8").strip() != tag[1:]:
            raise RuntimeError("VERSION no coincide con el tag; no se instalará.")
        return commit

    def image_command(self, image):
        write_json(self.override, {"services": {"server": {
            "build": {"context": str(self.source), "dockerfile": "Dockerfile"},
            "image": image}}})
        return [*self.base_command(), "-f", str(self.override)]

    def build(self, image, tag):
        command([*self.image_command(image), "build", "--build-arg", "ARCANA_VERSION=" + tag[1:], "server"])

    def start(self, image):
        command([*self.image_command(image), "up", "-d", "--no-build", "--no-deps", "--force-recreate", "server"])

    def wait_healthy(self, base, expected):
        for attempt in range(30):
            try:
                health = get_json(base + "/health")
                if health.get("status") == "ok" and health.get("version") == expected:
                    return
            except (OSError, ValueError):
                pass
            time.sleep(2)
        raise RuntimeError("El servidor no responde correctamente con la versión " + expected)

    def run(self):
        tag = stable_tag(get_json("https://api.github.com/repos/" + REPOSITORY + "/releases/latest"))
        container = self.container()
        if not container or not container["State"]["Running"]:
            log("Servidor detenido; se respeta su estado y no se instala ninguna actualización.")
            return
        current, humans, base = self.status(container)
        applied_file = self.state / "applied.json"
        applied = json.loads(applied_file.read_text()) if applied_file.exists() else {}
        if version(tag) <= max(version(current), version(applied.get("tag", "v0.0.0"))):
            log("Sin novedades: servidor " + current + ", última Release " + tag + ".")
            return
        if humans:
            log("Actualización " + tag + " aplazada: hay " + str(humans) + " jugadores conectados.")
            return
        log("Descargando y compilando " + tag + "; el servidor anterior continúa funcionando.")
        commit = self.fetch_source(tag)
        candidate = "arcana-arena-server:auto-" + tag
        self.build(candidate, tag)
        # La compilación puede tardar: volver a comprobar ocupación e instancia.
        latest_container = self.container()
        if not latest_container or latest_container["Id"] != container["Id"]:
            log("La instancia ha cambiado durante la compilación; actualización aplazada.")
            return
        _, humans, _ = self.status(latest_container)
        if humans:
            log("Han entrado jugadores durante la compilación; instalación aplazada.")
            return
        backup = "arcana-arena-server:previous"
        command(["docker", "image", "tag", container["Image"], backup])
        try:
            self.start(candidate)
            self.wait_healthy(base, tag[1:])
        except Exception as error:
            log("La actualización falló; restaurando " + current + ".")
            self.start(backup)
            self.wait_healthy(base, current)
            raise RuntimeError("Se restauró " + current + ": " + str(error)) from error
        write_json(applied_file, {"tag": tag, "commit": commit, "previous_version": current})
        log("Actualización completada y comprobada: " + tag + ".")


def install(checkout, compose_file=None):
    checkout = checkout.resolve(strict=True)
    compose = compose_command()
    arguments = [*compose, "--project-directory", str(checkout)]
    if compose_file:
        arguments += ["-f", str((checkout / compose_file).resolve(strict=True))]
    identifier = command([*arguments, "ps", "-q", "server"], capture=True)
    if not identifier or len(identifier.splitlines()) != 1:
        raise RuntimeError("Arranca primero el servidor con Docker Compose desde " + str(checkout))
    container = json.loads(command(["docker", "inspect", identifier], capture=True))[0]
    if not container["State"]["Running"]:
        raise RuntimeError("El servidor debe estar funcionando al instalar el actualizador.")
    labels = container["Config"]["Labels"]
    files = [str(Path(filename).resolve(strict=True))
             for filename in labels["com.docker.compose.project.config_files"].split(",")
             if Path(filename).resolve() != STATE / "compose.update.json"]
    if not files:
        raise RuntimeError("No se han encontrado los archivos Compose originales.")
    STATE.mkdir(mode=0o700, parents=True, exist_ok=True)
    PROGRAM.parent.mkdir(mode=0o755, parents=True, exist_ok=True)
    if Path(__file__).resolve() != PROGRAM:
        shutil.copyfile(__file__, PROGRAM)
    PROGRAM.chmod(0o755)
    write_json(CONFIG, {"checkout": str(checkout), "project": labels["com.docker.compose.project"], "compose_files": files})
    CONFIG.chmod(0o600)
    (UNITS / "arcana-server-update.service").write_text("""[Unit]
Description=Actualizar Arcana Arena desde la ultima Release estable
Wants=network-online.target
After=network-online.target docker.service
Requires=docker.service

[Service]
Type=oneshot
StateDirectory=arcana-server-updater
UMask=0077
ExecStart=/usr/bin/python3 /usr/local/lib/arcana-server-updater/update.py run
TimeoutStartSec=1h
""", encoding="utf-8")
    (UNITS / "arcana-server-update.timer").write_text("""[Unit]
Description=Comprobar actualizaciones estables de Arcana Arena cada 30 minutos

[Timer]
OnCalendar=*-*-* *:00,30:00
Persistent=true
RandomizedDelaySec=30
Unit=arcana-server-update.service

[Install]
WantedBy=timers.target
""", encoding="utf-8")
    command(["systemctl", "daemon-reload"])
    command(["systemctl", "enable", "--now", "arcana-server-update.timer"])
    log("Actualizador instalado: comprueba versiones estables cada 30 minutos y tras un periodo apagado.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    setup = sub.add_parser("install", help="Instalar el servicio y temporizador systemd")
    setup.add_argument("--checkout", type=Path, default=Path.cwd())
    setup.add_argument("--compose-file", help="Ejemplo: compose.lan.yaml")
    sub.add_parser("run", help="Comprobar e instalar una Release nueva")
    args = parser.parse_args()
    if os.geteuid() != 0:
        parser.error("Ejecuta este comando con sudo.")
    if args.action == "install":
        install(args.checkout, args.compose_file)
    else:
        STATE.mkdir(mode=0o700, parents=True, exist_ok=True)
        with (STATE / "update.lock").open("w") as lock:
            try:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                log("Ya hay una actualización en curso.")
                return
            Updater(json.loads(CONFIG.read_text(encoding="utf-8"))).run()


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError) as error:
        log("ERROR: " + str(error))
        sys.exit(1)
