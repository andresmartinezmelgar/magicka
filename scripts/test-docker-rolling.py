#!/usr/bin/env python3
"""Verifica NAT, control privado y dos generaciones con una imagen ya compilada."""
import argparse
import json
from pathlib import Path
import socket
import subprocess
import tempfile
import time
import urllib.error
import uuid
from arcana_rolling_update import get_json, write_json


def command(*args):
    return subprocess.check_output(["docker", *args], text=True).strip()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", required=True)
    parser.add_argument("--arch", required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    prefix = "arcana-rolling-test-" + uuid.uuid4().hex[:12]
    network, volume = prefix + "-network", prefix + "-data"
    names = []; token = "a" * 64
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0)); public_port = probe.getsockname()[1]
    with tempfile.TemporaryDirectory(prefix=prefix) as temporary:
        root = Path(temporary); root.chmod(0o755)
        try:
            command("network", "create", network); command("volume", "create", volume)
            peer = json.loads(command("network", "inspect", network))[0]["IPAM"]["Config"][0]["Gateway"]
            nodes = []
            for index in range(2):
                generation = uuid.uuid4().hex; name = prefix + "-" + str(index); names.append(name)
                folder = root / str(index); folder.mkdir(mode=0o755)
                role = dict(instanceId=generation, acceptingNewGames=index == 0, targetVersion="")
                write_json(folder / "admission.json", role, public=True)
                command("run", "-d", "--name", name, "--platform", "linux/" + args.arch, "--network", network,
                    "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true", "-p", "127.0.0.1::27015/tcp",
                    "--mount", "type=volume,src=" + volume + ",dst=/app/data",
                    "--mount", "type=bind,src=" + str(folder) + ",dst=/run/arcana-admission,readonly",
                    "-e", "ARCANA_ADMISSION_FILE=/run/arcana-admission/admission.json", "-e", "ARCANA_SERVER_GENERATION=" + generation,
                    "-e", "ARCANA_MANAGEMENT_TOKEN=" + token, "-e", "ARCANA_MANAGEMENT_PEER=" + peer,
                    "-e", "ARCANA_DISCOVERY_ENABLED=false", "-e", "ARCANA_ADVERTISED_PORT=" + str(public_port), args.image)
                port = int(command("port", name, "27015/tcp").split(":")[-1])
                base = "http://127.0.0.1:" + str(port)
                last_error = None
                for attempt in range(120):
                    try:
                        status = get_json(base + "/internal/lifecycle", secret=token)
                        if status["instanceId"] == generation and status["version"] == args.version:
                            break
                    except (OSError, ValueError) as error:
                        last_error = str(error)
                    time.sleep(.5)
                else:
                    subprocess.run(["docker", "logs", "--tail", "30", name], check=False)
                    raise RuntimeError("El backend Docker no responde al control privado: " + str(last_error))
                nodes.append(dict(id=generation, version=args.version, port=port))
            gateway = prefix + "-gateway"; names.append(gateway)
            route = dict(phase="ready", active=nodes[0]["id"], targetVersion="", nodes=nodes)
            routes = root / "routes"; routes.mkdir(mode=0o755); write_json(routes / "routes.json", route, public=True)
            command("run", "-d", "--name", gateway, "--platform", "linux/" + args.arch, "--network", "host",
                "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true",
                "--mount", "type=bind,src=" + str(routes) + ",dst=/routing,readonly", "-e", "ARCANA_GATEWAY_CONFIG=/routing/routes.json",
                "-e", "ARCANA_DISCOVERY_ENABLED=false", "-e", "ARCANA_GATEWAY_BIND=127.0.0.1", "-e", "GAME_PORT=" + str(public_port), args.image, "--gateway")
            base = "http://127.0.0.1:" + str(public_port)
            for attempt in range(120):
                try:
                    if get_json(base + "/health").get("role") == "gateway":
                        break
                except (OSError, ValueError):
                    pass
                time.sleep(.5)
            else:
                raise RuntimeError("La entrada Docker no responde.")
            assert get_json(base + "/info")["instanceId"] == nodes[0]["id"]
            try:
                get_json(base + "/internal/lifecycle", secret=token)
                raise AssertionError("El control interno es accesible por la entrada pública.")
            except urllib.error.HTTPError as error:
                assert error.code == 404
            route.update(phase="starting", targetVersion=args.version); write_json(routes / "routes.json", route, public=True)
            try:
                get_json(base + "/rooms"); raise AssertionError("Se admite crear partidas durante el arranque.")
            except urllib.error.HTTPError as error:
                assert error.code == 503 and json.load(error)["code"] == "server_starting"
            for index, accepting in [(0, False), (1, True)]:
                value = dict(instanceId=nodes[index]["id"], acceptingNewGames=accepting, targetVersion=args.version if not accepting else "")
                write_json(root / str(index) / "admission.json", value, public=True)
                response = get_json(f"http://127.0.0.1:{nodes[index]['port']}/internal/admission", value, token)
                assert response["acceptingNewGames"] is accepting
            route.update(phase="ready", active=nodes[1]["id"]); write_json(routes / "routes.json", route, public=True)
            assert get_json(base + "/info")["instanceId"] == nodes[1]["id"]
            command("exec", names[0], "sh", "-c", "printf shared > /app/data/rolling-probe")
            assert command("exec", names[1], "cat", "/app/data/rolling-probe") == "shared"
            command("rm", "-f", names[0]); names.pop(0)
            route["nodes"] = [nodes[1]]; write_json(routes / "routes.json", route, public=True)
            assert get_json(base + "/rooms")["server"]["instanceId"] == nodes[1]["id"]
            print("DOCKER_ROLLING_OK: control por NAT, dos generaciones, entrada estable y volumen compartido.")
        finally:
            for name in reversed(names):
                subprocess.run(["docker", "rm", "-f", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            subprocess.run(["docker", "volume", "rm", volume], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            subprocess.run(["docker", "network", "rm", network], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


if __name__ == "__main__":
    main()
