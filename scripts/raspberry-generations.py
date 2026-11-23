#!/usr/bin/env python3
"""Instala y supervisa actualizaciones de Arcana Arena sin cortar partidas."""
import argparse
import fcntl
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import signal
import subprocess
import sys
import time
from arcana_rolling_update import (CONFIG, PROGRAM, STATE, UNITS, RollingUpdater,
    command, compose_command, inspect, log, version, write_json)


def prepare(checkout):
    checkout = checkout.resolve(strict=True)
    name = (checkout / "VERSION").read_text().strip(); version(name)
    commit = command(["git", "-C", str(checkout), "rev-parse", "HEAD"], capture=True)
    if not re.fullmatch(r"[0-9a-f]{40,64}", commit):
        raise RuntimeError("No se pudo identificar el código de la instalación.")
    if not (checkout / "src/Arcana.Server/RollingGateway.cs").is_file():
        raise RuntimeError("El código todavía no contiene la entrada por generaciones.")
    image = "arcana-arena-server:bootstrap-" + commit[:16]
    command(["docker", "build", "--build-arg", "ARCANA_VERSION=" + name, "-t", image, str(checkout)])
    return image, commit, name


def legacy_cron_conflict(checkout):
    if not shutil.which("crontab"):
        return
    for user in {"root", os.environ.get("SUDO_USER", "root")}:
        result = subprocess.run(["crontab", "-u", user, "-l"], text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, timeout=20)
        if result.returncode == 0:
            for line in result.stdout.splitlines():
                if not line.lstrip().startswith("#") and str(checkout) in line and "docker" in line and "compose" in line:
                    raise RuntimeError("Desactiva primero la tarea cron antigua de Arcana en la cuenta " + user + "; no deben actuar dos actualizadores.")


def install(checkout, compose_file=None, backend_ports=(27017, 27019)):
    checkout = checkout.resolve(strict=True)
    STATE.mkdir(mode=0o700, parents=True, exist_ok=True)
    config = json.loads(CONFIG.read_text()) if CONFIG.exists() else None
    if not (config and config.get("schema") == 2 and (STATE / "rolling.json").exists()):
        legacy_cron_conflict(checkout)
        arguments = [*compose_command(), "--project-directory", str(checkout)]
        if compose_file:
            arguments += ["-f", str((checkout / compose_file).resolve(strict=True))]
        ids = command([*arguments, "ps", "-a", "-q", "server"], capture=True).splitlines()
        if len(ids) != 1:
            raise RuntimeError("Debe existir un contenedor Compose server. Conserva el contenedor y sus volúmenes.")
        template = inspect(ids[0])
        if not template or template["State"]["Running"]:
            raise RuntimeError("La primera instalación requiere el servidor anterior detenido. Termina sus partidas, detén solo server y repite install; no se detendrá automáticamente.")
        subprocess.run(["systemctl", "disable", "--now", "arcana-server-update.timer"], check=False,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
        subprocess.run(["systemctl", "stop", "arcana-server-update.service"], check=False,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
        env = dict(e.split("=", 1) for e in template["Config"]["Env"] if "=" in e)
        port = int(env.get("GAME_PORT", "27015")); discovery = int(env.get("DISCOVERY_PORT", "27016"))
        host = template["HostConfig"].get("NetworkMode") == "host"
        bindings = template["HostConfig"].get("PortBindings") or {}
        tcp = bindings.get(str(port) + "/tcp"); udp = bindings.get(str(discovery) + "/udp")
        if not host and (not tcp or len(tcp) != 1):
            raise RuntimeError("No se puede determinar un único puerto público TCP.")
        bind = "0.0.0.0" if host else tcp[0].get("HostIp") or "0.0.0.0"
        if not host:
            port = int(tcp[0]["HostPort"])
            if udp:
                discovery = int(udp[0]["HostPort"])
        if not all(1024 <= p <= 65535 for p in (port, discovery)):
            raise RuntimeError("Los puertos públicos deben estar entre 1024 y 65535.")
        project = template["Config"]["Labels"]["com.docker.compose.project"]
        if not re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,48}", project):
            raise RuntimeError("Nombre de proyecto Compose no admitido.")
        config = dict(schema=2, checkout=str(checkout), project=project, port=port, discovery_port=discovery,
                      bind=bind, discovery_bind="0.0.0.0" if host else (udp[0].get("HostIp") or "0.0.0.0") if udp else "127.0.0.1",
                      discovery_enabled=host or bool(udp), backend_ports=list(backend_ports), management_token=secrets.token_hex(32), template=template)
        updater = RollingUpdater(config)
        image, commit, name = prepare(checkout)
        current = inspect(template["Id"])
        if not current or current["State"]["Running"]:
            raise RuntimeError("El estado del servidor anterior ha cambiado; no se sustituirá.")
        write_json(CONFIG, config)
        updater.bootstrap(image, commit, name)
    elif Path(config["checkout"]) != checkout:
        raise RuntimeError("Esta Raspberry ya supervisa otro checkout de Arcana.")
    elif json.loads((STATE / "rolling.json").read_text()).get("bootstrap"):
        updater = RollingUpdater(config)
        node = updater.load()["active"]
        updater.bootstrap(node["image"], node["commit"], node["version"])
    PROGRAM.parent.mkdir(mode=0o755, parents=True, exist_ok=True)
    for source, target in [(Path(__file__), PROGRAM),
                           (Path(__file__).with_name("arcana_rolling_update.py"), PROGRAM.with_name("arcana_rolling_update.py")),
                           (Path(__file__).with_name("server_release.py"), PROGRAM.with_name("server_release.py"))]:
        if source.resolve() != target.resolve():
            shutil.copyfile(source, target)
        target.chmod(0o755)
    subprocess.run(["systemctl", "disable", "--now", "arcana-server-update.timer"], check=False,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
    (UNITS / "arcana-server-update.service").write_text("""[Unit]
Description=Supervisar generaciones de Arcana Arena sin cortar partidas
Wants=network-online.target
After=network-online.target docker.service
Requires=docker.service

[Service]
Type=simple
StateDirectory=arcana-server-updater
UMask=0077
ExecStart=/usr/bin/python3 /usr/local/lib/arcana-server-updater/update.py watch
Restart=on-failure
RestartSec=15
TimeoutStopSec=15

[Install]
WantedBy=multi-user.target
""", encoding="utf-8")
    command(["systemctl", "daemon-reload"])
    command(["systemctl", "enable", "arcana-server-update.service"])
    command(["systemctl", "restart", "arcana-server-update.service"])
    log("Supervisor instalado: novedades cada 30 minutos y retirada de generaciones cada 5 segundos.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    for name in ("install", "prepare"):
        setup = sub.add_parser(name)
        setup.add_argument("--checkout", type=Path, default=Path.cwd())
        if name == "install":
            setup.add_argument("--compose-file")
            setup.add_argument("--backend-ports", default="27017,27019")
    sub.add_parser("run")
    sub.add_parser("watch")
    sub.add_parser("status")
    args = parser.parse_args()
    if os.geteuid() != 0:
        parser.error("Ejecuta este comando con sudo.")
    if args.action == "prepare":
        prepare(args.checkout); return
    if args.action == "install":
        install(args.checkout, args.compose_file, tuple(map(int, args.backend_ports.split(",")))); return
    STATE.mkdir(mode=0o700, parents=True, exist_ok=True)
    updater = RollingUpdater(json.loads(CONFIG.read_text()))
    if args.action == "status":
        data = updater.load()
        print(json.dumps({"phase": data["phase"], "active": updater.lifecycle(data["active"]),
            "retiring": [updater.lifecycle(n) for n in data["retiring"] if not n.get("staged")]}, indent=2))
        return
    with (STATE / "update.lock").open("w") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            if args.action == "run":
                (STATE / "check-now").touch(); log("Comprobación inmediata solicitada al supervisor."); return
            raise RuntimeError("Ya hay otro supervisor activo.")
        if args.action == "run":
            updater.run(force_check=True); return
        stopped = False
        def stop(_signum, _frame):
            nonlocal stopped
            stopped = True
        signal.signal(signal.SIGTERM, stop); signal.signal(signal.SIGINT, stop)
        while not stopped:
            marker = STATE / "check-now"; force = marker.exists(); marker.unlink(missing_ok=True)
            try:
                updater.run(force_check=force)
            except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError) as error:
                log("ERROR: " + str(error))
            for _ in range(5):
                if stopped:
                    break
                time.sleep(1)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError) as error:
        log("ERROR: " + str(error)); sys.exit(1)
