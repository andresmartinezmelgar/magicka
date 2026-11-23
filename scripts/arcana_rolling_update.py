"""Actualización por generaciones; nunca sustituye un proceso que tiene partidas activas."""
import ipaddress
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import time
import urllib.request
import urllib.parse
import uuid

from server_release import TAG, stable_tag, version


REPOSITORY = "andresmartinezmelgar/magicka"
STATE = Path("/var/lib/arcana-server-updater")
CONFIG = Path("/etc/arcana-server-updater.json")
PROGRAM = Path("/usr/local/lib/arcana-server-updater/update.py")
UNITS = Path("/etc/systemd/system")
OWNER = "io.arcana.rolling"


def log(message):
    print(time.strftime("%Y-%m-%d %H:%M:%S"), message, flush=True)


def command(arguments, capture=False, cwd=None, timeout=3600):
    result = subprocess.run(arguments, check=True, text=True, cwd=cwd, timeout=timeout,
                            stdout=subprocess.PIPE if capture else None)
    return result.stdout.strip() if capture else ""


def write_json(path, value, public=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as output:
        json.dump(value, output, indent=2); output.write("\n")
        output.flush(); os.fsync(output.fileno())
    temporary.chmod(0o644 if public else 0o600)
    temporary.replace(path)
    descriptor = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def get_json(url, data=None, secret=None):
    headers = {"Accept": "application/json", "User-Agent": "Arcana-Rolling-Updater"}
    if secret:
        headers["X-Arcana-Management"] = secret
    if data is not None:
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(url, data=None if data is None else json.dumps(data).encode(), headers=headers)
    host = urllib.parse.urlsplit(url).hostname
    try:
        local = ipaddress.ip_address(host).is_loopback
    except ValueError:
        local = False
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({})) if local else urllib.request.build_opener()
    with opener.open(request, timeout=20) as response:
        content = response.read(1024 * 1024 + 1)
    if len(content) > 1024 * 1024:
        raise RuntimeError("Respuesta HTTP demasiado grande.")
    return json.loads(content)


def compose_command():
    for candidate in (["docker", "compose"], ["docker-compose"]):
        try:
            subprocess.run([*candidate, "version"], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=20)
            return candidate
        except (OSError, subprocess.SubprocessError):
            pass
    raise RuntimeError("Docker Compose no está disponible.")


def inspect(name):
    result = subprocess.run(["docker", "inspect", name], text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
    if result.returncode:
        if "No such" in result.stderr:
            return None
        raise RuntimeError("No se pudo inspeccionar el contenedor " + name)
    values = json.loads(result.stdout)
    if len(values) != 1:
        raise RuntimeError("Identidad de contenedor ambigua.")
    return values[0]


class RollingUpdater:
    def __init__(self, config, state=STATE):
        if config.get("schema") != 2:
            raise RuntimeError("El actualizador anterior necesita la instalación inicial del modo de generaciones; no se sustituirá el servidor en marcha.")
        self.config = config
        self.state = state
        self.file = state / "rolling.json"
        self.source = state / "source"
        self.public = state / "routing"
        self.public.mkdir(parents=True, exist_ok=True); self.public.chmod(0o755)
        ports = config["backend_ports"]
        if len(ports) != 2 or len(set(ports)) != 2 or any(type(p) is not int or not 1024 <= p <= 65535 or p == config["port"] for p in ports):
            raise RuntimeError("Se necesitan dos puertos privados diferentes del puerto público.")
        template = config["template"]
        environment = dict(e.split("=", 1) for e in template["Config"]["Env"] if "=" in e)
        for key, fallback in (("ARCANA_REWARD_OUTBOX", "/app/data/rewards"), ("ARCANA_MATCH_STATISTICS_DIR", "/app/data/match-statistics")):
            directory = PurePosixPath(environment.get(key) or fallback)
            if not directory.is_absolute() or ".." in directory.parts or not any(
                m.get("RW") and m["Type"] in ("bind", "volume") and directory.is_relative_to(PurePosixPath(m["Destination"]))
                for m in template.get("Mounts", [])):
                raise RuntimeError("La cola " + key + " necesita un montaje persistente y compartido con escritura.")

    def load(self):
        data = json.loads(self.file.read_text())
        nodes = [data["active"], *data["retiring"]] + ([data["candidate"]] if data.get("candidate") else [])
        if data.get("schema") != 2 or len(nodes) > 2 or len({n["id"] for n in nodes}) != len(nodes) or len({n["port"] for n in nodes}) != len(nodes):
            raise RuntimeError("Estado de generaciones inválido; no se eliminará ningún contenedor.")
        for node in nodes:
            if not re.fullmatch(r"[0-9a-f]{32}", node["id"]) or node["port"] not in self.config["backend_ports"] or node["name"] != self.config["project"] + "-generation-" + node["id"]:
                raise RuntimeError("Identidad de generación inválida.")
            version(node["version"])
        return data

    def save(self, data):
        write_json(self.file, data)

    def publish(self, data):
        # El fichero visible para el proxy no contiene credenciales ni configuración Docker.
        nodes = [data["active"], *(n for n in data["retiring"] if not n.get("staged"))]
        value = {"phase": data["phase"], "active": data["active"]["id"], "targetVersion": data.get("target", ""),
                 "nodes": [dict(id=n["id"], version=n["version"], port=n["port"]) for n in nodes]}
        file = self.public / "routes.json"
        if file.exists() and json.loads(file.read_text()) == value:
            return
        write_json(file, value, public=True)

    def managed(self, node):
        container = inspect(node["name"])
        if container is not None:
            labels = container["Config"].get("Labels") or {}
            if labels.get(OWNER) != self.config["project"] or labels.get(OWNER + ".generation") != node["id"] or container["Config"].get("Image") != node["image"] or node.get("container_id") is not None and container["Id"] != node["container_id"]:
                raise RuntimeError("El contenedor no coincide con la generación registrada; se conserva: " + node["name"])
            # Recupera un create confirmado por Docker justo antes de interrumpirse el programa.
            node["container_id"] = container["Id"]
        return container

    def lifecycle(self, node):
        result = get_json(f"http://127.0.0.1:{node['port']}/internal/lifecycle", secret=self.config["management_token"])
        if result.get("instanceId") != node["id"] or result.get("version") != node["version"] or type(result.get("acceptingNewGames")) is not bool or type(result.get("safeToStop")) is not bool:
            raise RuntimeError("El estado pertenece a otra instancia o versión.")
        for key in ("activeGames", "reconnectingPlayers", "connections", "pendingWrites"):
            if type(result.get(key)) is not int or result[key] < 0:
                raise RuntimeError("No se puede verificar la ocupación de la generación.")
        return result

    def admission(self, node, accepting, target=""):
        value = {"instanceId": node["id"], "acceptingNewGames": accepting, "targetVersion": target}
        directory = self.state / "nodes" / node["id"]
        directory.mkdir(parents=True, exist_ok=True); directory.chmod(0o755)
        write_json(directory / "admission.json", value, public=True)
        result = get_json(f"http://127.0.0.1:{node['port']}/internal/admission", value, self.config["management_token"])
        if result.get("instanceId") != node["id"] or result.get("version") != node["version"] or result.get("acceptingNewGames") is not accepting:
            raise RuntimeError("El servidor no confirmó el cambio de admisión.")

    def healthy(self, node, require_closed=True):
        for _ in range(45):
            container = self.managed(node)
            if container is None or not container["State"]["Running"]:
                raise RuntimeError("El contenedor candidato se ha detenido.")
            try:
                health = get_json(f"http://127.0.0.1:{node['port']}/health")
                status = self.lifecycle(node)
                if health.get("status") == "ok" and health.get("version") == node["version"] and (not require_closed or status["acceptingNewGames"] is False):
                    return
            except (OSError, ValueError, RuntimeError):
                pass
            time.sleep(2)
        raise RuntimeError("La nueva versión no supera la comprobación de arranque.")

    def fetch_source(self, tag):
        self.source.mkdir(parents=True, exist_ok=True)
        if not (self.source / ".git").exists():
            command(["git", "init", str(self.source)])
            command(["git", "-C", str(self.source), "remote", "add", "origin", "https://github.com/" + REPOSITORY + ".git"])
        command(["git", "-C", str(self.source), "fetch", "--depth=1", "origin", "refs/tags/" + tag + ":refs/tags/" + tag])
        commit = command(["git", "-C", str(self.source), "rev-parse", tag + "^{commit}"], capture=True)
        command(["git", "-C", str(self.source), "checkout", "--detach", "--force", commit])
        if (self.source / "VERSION").read_text().strip() != tag[1:]:
            raise RuntimeError("VERSION no coincide con el tag.")
        return commit

    def build(self, image, tag):
        command(["docker", "build", "--build-arg", "ARCANA_VERSION=" + tag[1:], "-t", image, str(self.source)])

    def node(self, version_name, image, commit, occupied=()):
        identifier = uuid.uuid4().hex
        port = next((p for p in self.config["backend_ports"] if p not in occupied), None)
        if port is None:
            raise RuntimeError("No hay una plaza libre para otra generación.")
        return dict(id=identifier, name=self.config["project"] + "-generation-" + identifier,
                    version=version_name, image=image, commit=commit, port=port)

    def create_node(self, node):
        template = self.config["template"]
        network = template.get("HostConfig", {}).get("NetworkMode", "host")
        peer = ""
        if network != "host":
            networks = template.get("NetworkSettings", {}).get("Networks", {})
            if network == "default":
                network = "bridge"
            if network not in networks:
                raise RuntimeError("No se puede conservar la red Docker original.")
            peer = networks[network].get("Gateway")
            if not peer:
                # Docker vacía Gateway en inspect al detener el contenedor original.
                details = json.loads(command(["docker", "network", "inspect", network], capture=True))[0]
                peer = next((c["Gateway"] for c in details["IPAM"]["Config"] if c.get("Gateway") and
                             ipaddress.ip_address(c["Gateway"]).version == 4), None)
            if not peer:
                raise RuntimeError("No se pudo determinar la puerta de enlace IPv4 de la red original.")
        environment = dict(entry.split("=", 1) for entry in template["Config"]["Env"] if "=" in entry)
        environment.update(GAME_PORT=str(node["port"]), ARCANA_SERVER_GENERATION=node["id"],
            ARCANA_SERVER_INSTANCE=node["id"], ARCANA_MANAGEMENT_TOKEN=self.config["management_token"],
            ARCANA_DISCOVERY_ENABLED="false", ARCANA_LOOPBACK_ONLY=str(network == "host").lower(), ARCANA_AUTO_PUBLIC_HOST="false",
            ARCANA_MANAGEMENT_PEER=peer,
            ARCANA_ADVERTISED_PORT=str(self.config["port"]), ARCANA_ADVERTISED_DISCOVERY_PORT=str(self.config["discovery_port"]),
            ARCANA_ADMISSION_FILE="/run/arcana-admission/admission.json")
        if not environment.get("ARCANA_REWARD_OUTBOX"):
            environment["ARCANA_REWARD_OUTBOX"] = "/app/data/rewards"
        if not environment.get("ARCANA_MATCH_STATISTICS_DIR"):
            environment["ARCANA_MATCH_STATISTICS_DIR"] = "/app/data/match-statistics"
        environment["ARCANA_LOG_DIRECTORY"] = environment.get("ARCANA_LOG_DIRECTORY", "/app/logs").rstrip("/") + "/" + node["id"]
        directory = self.state / "nodes" / node["id"]; directory.mkdir(parents=True, exist_ok=True); directory.chmod(0o755)
        write_json(directory / "admission.json", {"instanceId": node["id"], "acceptingNewGames": False, "targetVersion": ""}, public=True)
        envfile = self.state / (node["id"] + ".env")
        if any("\n" in k + v or "\r" in k + v for k, v in environment.items()):
            raise RuntimeError("La configuración contiene un valor multilínea que Docker --env-file no puede conservar.")
        descriptor = os.open(envfile, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        with os.fdopen(descriptor, "w") as output:
            output.write("\n".join(k + "=" + v for k, v in environment.items()) + "\n")
        arguments = ["docker", "create", "--name", node["name"], "--network", network, "--restart", "unless-stopped",
            "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true", "--label", OWNER + "=" + self.config["project"],
            "--label", OWNER + ".generation=" + node["id"], "--env-file", str(envfile)]
        if network != "host":
            arguments += ["--publish", f"127.0.0.1:{node['port']}:{node['port']}/tcp"]
        if template["Config"].get("User"):
            arguments += ["--user", template["Config"]["User"]]
        if template["Config"].get("WorkingDir"):
            arguments += ["--workdir", template["Config"]["WorkingDir"]]
        for mount in template.get("Mounts", []):
            if mount["Destination"] == "/run/arcana-admission":
                continue
            kind = mount["Type"]
            if kind not in ("bind", "volume"):
                raise RuntimeError("Tipo de montaje no admitido para clonar el servidor: " + kind)
            source = mount["Name"] if kind == "volume" else mount["Source"]
            if any("," in value for value in (source, mount["Destination"])):
                raise RuntimeError("Un montaje contiene comas; no se cambiará su interpretación.")
            arguments += ["--mount", f"type={kind},src={source},dst={mount['Destination']}" + ("" if mount["RW"] else ",readonly")]
        arguments += ["--mount", "type=bind,src=" + str(directory) + ",dst=/run/arcana-admission,readonly"]
        resources = template.get("HostConfig", {})
        if resources.get("ReadonlyRootfs"):
            arguments += ["--read-only"]
        for host in resources.get("ExtraHosts") or []:
            arguments += ["--add-host", host]
        for value in resources.get("Dns") or []:
            arguments += ["--dns", value]
        for item in resources.get("Ulimits") or []:
            arguments += ["--ulimit", f"{item['Name']}={item['Soft']}:{item['Hard']}"]
        for key, option in (("Memory", "--memory"), ("MemorySwap", "--memory-swap"), ("NanoCpus", "--cpus"), ("PidsLimit", "--pids-limit")):
            value = resources.get(key, 0)
            if value and value > 0:
                arguments += [option, str(value / 1_000_000_000 if key == "NanoCpus" else value)]
        entrypoint = template["Config"].get("Entrypoint") or []
        if entrypoint:
            arguments += ["--entrypoint", entrypoint[0]]
        arguments += [node["image"], *entrypoint[1:], *(template["Config"].get("Cmd") or [])]
        try:
            node["container_id"] = command(arguments, capture=True)
        finally:
            envfile.unlink(missing_ok=True)
        command(["docker", "start", node["container_id"]])

    def remove(self, node, staged=False):
        container = self.managed(node)
        if container is None:
            return
        if not staged:
            status = self.lifecycle(node)
            if status["acceptingNewGames"] or status["activeGames"] or status["pendingWrites"] or not status["safeToStop"]:
                raise RuntimeError("Se conserva la generación: todavía no es seguro cerrarla.")
            command(["docker", "stop", "--time", "30", container["Id"]])
            command(["docker", "rm", container["Id"]])
        else:
            # Solo una instancia de preparación que nunca se ha publicado.
            command(["docker", "rm", "--force", container["Id"]])

    def reap(self, data):
        for old in data["retiring"][:]:
            if old.get("staged"):
                self.remove(old, staged=True)
                data["retiring"].remove(old); self.save(data); self.publish(data)
                continue
            if self.managed(old) is not None:
                status = self.lifecycle(old)
                if status["acceptingNewGames"]:
                    self.admission(old, False, data["active"]["version"])
                    continue
                if not status["safeToStop"] or status["activeGames"] or status["pendingWrites"]:
                    continue
                self.remove(old)
            data["retiring"].remove(old); self.save(data); self.publish(data)
            log("Generación " + old["version"] + " terminada y contenedor retirado; se conservan sus volúmenes.")

    def promote(self, data):
        candidate = data["candidate"]
        self.admission(candidate, True)
        previous = data["active"]
        data.update(active=candidate, retiring=[previous], candidate=None, phase="ready", target="", applied_tag="v" + candidate["version"])
        self.save(data); self.publish(data)
        log("Versión " + candidate["version"] + " disponible para nuevas partidas; " + previous["version"] + " termina las suyas.")

    def rollback(self, data):
        candidate = data.get("candidate")
        self.admission(data["active"], True)
        if candidate:
            candidate["staged"] = True; data["retiring"].append(candidate)
        data.update(candidate=None, phase="ready", target="")
        # Primero publicar de nuevo el proceso conocido, después retirar el candidato no expuesto.
        self.save(data); self.publish(data)
        self.reap(data)
        log("La nueva versión no estaba lista; se vuelve a admitir partidas en " + data["active"]["version"] + ".")

    def reconcile(self, data):
        if data["phase"] == "starting":
            candidate = data.get("candidate")
            if candidate and self.managed(candidate) is not None:
                try:
                    self.admission(data["active"], False, data["target"])
                    self.healthy(candidate, require_closed=False); self.promote(data)
                except Exception:
                    if data.get("candidate") is not None:
                        self.rollback(data)
                    raise
            else:
                self.rollback(data)
        else:
            if not self.lifecycle(data["active"])["acceptingNewGames"]:
                self.admission(data["active"], True)
            # Completa una conmutación interrumpida entre la escritura privada y la del proxy.
            self.publish(data)
        self.reap(data)

    def update(self, data, tag):
        if version(tag) <= max(version(data["active"]["version"]), version(data.get("applied_tag", "v0.0.0"))):
            return
        if data["retiring"]:
            data["queued_tag"] = tag; self.save(data)
            log("Versión " + tag + " en espera: se conservan como máximo dos generaciones de juego.")
            return
        data.update(phase="starting", target=tag[1:]); self.save(data); self.publish(data)
        try:
            self.admission(data["active"], False, tag[1:])
            log("Iniciando servidor con la nueva versión " + tag + "; continúan las partidas existentes.")
            commit = self.fetch_source(tag); image = "arcana-arena-server:auto-" + tag
            self.build(image, tag)
            candidate = self.node(tag[1:], image, commit, [data["active"]["port"]])
            data["candidate"] = candidate; self.save(data)
            self.create_node(candidate); self.save(data)
            self.healthy(candidate); self.promote(data)
        except Exception:
            if data["phase"] == "starting":
                self.rollback(data)
            # Si ya se hizo pública, nunca destruirla ni deshacer partidas nuevas.
            raise

    def run(self, force_check=False):
        data = self.load()
        gateway = inspect(self.config["project"] + "-gateway")
        if gateway is not None:
            labels = gateway["Config"].get("Labels") or {}
            if gateway["Id"] != data.get("gateway_id") or labels.get(OWNER) != self.config["project"] or labels.get(OWNER + ".role") != "gateway":
                raise RuntimeError("La entrada estable no coincide con la registrada; no se modificará.")
        current = self.managed(data["active"])
        if gateway is None or not gateway["State"]["Running"] or current is None or not current["State"]["Running"]:
            log("La entrada o la generación activa está detenida; se respeta ese estado.")
            return
        self.reconcile(data)
        queued = data.get("queued_tag")
        if queued and not data["retiring"]:
            data.pop("queued_tag"); self.save(data); force_check = True
        now = time.time()
        if not force_check and now < data.get("next_check", 0):
            return
        data["next_check"] = now + 1800; self.save(data)
        tag = stable_tag(get_json("https://api.github.com/repos/" + REPOSITORY + "/releases/latest"))
        self.update(data, tag)

    def bootstrap(self, image, commit, current_version):
        if self.file.exists():
            data = self.load()
            if not data.get("bootstrap"):
                raise RuntimeError("Ya existe una instalación por generaciones.")
        else:
            node = self.node(current_version, image, commit)
            data = dict(schema=2, active=node, retiring=[], candidate=None, phase="starting", target=current_version,
                        applied_tag="v" + current_version, next_check=0, bootstrap=True)
            self.save(data)
        node = data["active"]
        container = self.managed(node)
        if container is None:
            self.create_node(node); self.save(data)
        elif not container["State"]["Running"]:
            if data["phase"] != "starting":
                raise RuntimeError("La generación ya publicada está detenida; no se arrancará automáticamente.")
            command(["docker", "start", container["Id"]])
        self.healthy(node, require_closed=False)
        self.publish(data)
        name = self.config["project"] + "-gateway"
        gateway = inspect(name)
        if gateway is not None:
            labels = gateway["Config"].get("Labels") or {}
            if labels.get(OWNER) != self.config["project"] or labels.get(OWNER + ".role") != "gateway" or data.get("gateway_id") not in (None, gateway["Id"]):
                raise RuntimeError("Ya existe otra entrada con ese nombre; no se sustituirá.")
            data["gateway_id"] = gateway["Id"]; self.save(data)
        else:
            gateway_id = command(["docker", "create", "--name", name, "--network", "host",
                "--restart", "unless-stopped", "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true",
                "--label", OWNER + "=" + self.config["project"], "--label", OWNER + ".role=gateway",
                "--env", "GAME_PORT=" + str(self.config["port"]), "--env", "DISCOVERY_PORT=" + str(self.config["discovery_port"]),
                "--env", "ARCANA_GATEWAY_BIND=" + self.config.get("bind", "0.0.0.0"),
                "--env", "ARCANA_DISCOVERY_ENABLED=" + str(self.config.get("discovery_enabled", True)).lower(),
                "--env", "ARCANA_GATEWAY_DISCOVERY_BIND=" + self.config.get("discovery_bind", "0.0.0.0"),
                "--env", "ARCANA_GATEWAY_CONFIG=/routing/routes.json", "--mount",
                "type=bind,src=" + str(self.public) + ",dst=/routing,readonly", node["image"], "--gateway"], capture=True)
            data["gateway_id"] = gateway_id; self.save(data)
            gateway = inspect(gateway_id)
        if gateway is None:
            raise RuntimeError("La entrada no se ha creado correctamente; repite install para recuperar la preparación.")
        if not gateway["State"]["Running"]:
            if data["phase"] != "starting":
                raise RuntimeError("La entrada ya publicada está detenida; no se arrancará automáticamente.")
            command(["docker", "start", gateway["Id"]])
        host = self.config.get("bind", "0.0.0.0")
        if host in ("0.0.0.0", "::"):
            host = "127.0.0.1" if host == "0.0.0.0" else "[::1]"
        elif ":" in host:
            host = "[" + host + "]"
        for attempt in range(30):
            try:
                health = get_json(f"http://{host}:{self.config['port']}/health")
                if health.get("role") == "gateway" and health.get("instanceId") == node["id"]:
                    break
            except (OSError, ValueError):
                pass
            time.sleep(1)
        else:
            raise RuntimeError("La entrada no responde. El estado se conserva; repite install después de resolver el arranque.")
        self.admission(node, True)
        data.update(phase="ready", target=""); self.save(data); self.publish(data)
        command(["docker", "update", "--restart=no", self.config["template"]["Id"]])
        data["bootstrap"] = False; self.save(data)
        log("Entrada estable y primera generación listas; se mantiene la URL y el puerto públicos.")
