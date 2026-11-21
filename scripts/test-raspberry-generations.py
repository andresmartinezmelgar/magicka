#!/usr/bin/env python3
"""Pruebas del supervisor con Docker y red simulados; no compila ni cambia servidores."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import arcana_rolling_update as updater


class FakeUpdater(updater.RollingUpdater):
    def __init__(self, state):
        config = dict(schema=2, project="arcana-test", backend_ports=[27017, 27019], port=27015, discovery_port=27016,
            management_token="a" * 64, template={"Config": {"Env": ["FIREBASE_API_KEY=private-value"], "User": "app",
            "Entrypoint": ["dotnet", "Arcana.Server.dll"], "Cmd": None}, "Mounts": [
            {"Type": "volume", "Name": "original-data", "Destination": "/app/data", "RW": True},
            {"Type": "bind", "Source": "/etc/arcana/firebase.json", "Destination": "/app/firebase.json", "RW": False}],
            "HostConfig": {"Memory": 700000000, "NanoCpus": 1000000000}})
        super().__init__(config, state)
        self.events = []
        self.containers = {}
        self.statuses = {}
        self.fail_build = self.fail_health = False
        active = self.node("0.29.65", "server:old", "old-commit")
        self.create_node(active)
        self.statuses[active["id"]].update(acceptingNewGames=True, activeGames=2)
        self.gateway = config["project"] + "-gateway"
        self.containers[self.gateway] = dict(Id="gateway-id", State={"Running": True},
            Config={"Labels": {updater.OWNER: config["project"], updater.OWNER + ".role": "gateway"}})
        self.data = dict(schema=2, active=active, retiring=[], candidate=None, phase="ready",
                         target="", gateway_id="gateway-id", applied_tag="v0.29.65", next_check=0)
        self.save(self.data); self.publish(self.data); self.events.clear()

    def lookup(self, name):
        return self.containers.get(name) or next((c for c in self.containers.values() if c["Id"] == name), None)

    def lifecycle(self, node):
        value = self.statuses[node["id"]]
        if isinstance(value, list):
            return value.pop(0)
        return value.copy()

    def admission(self, node, accepting, target=""):
        self.events.append(("admit", node["version"], accepting))
        self.statuses[node["id"]]["acceptingNewGames"] = accepting

    def fetch_source(self, tag):
        self.events.append(("fetch", tag)); return "new-commit"

    def build(self, image, tag):
        self.events.append(("build", tag))
        if self.fail_build:
            raise RuntimeError("Compilación no disponible")

    def create_node(self, node):
        node["container_id"] = "container-" + node["id"]
        self.containers[node["name"]] = dict(Id=node["container_id"], State={"Running": True}, Config={
            "Image": node["image"], "Labels": {updater.OWNER: self.config["project"], updater.OWNER + ".generation": node["id"]}})
        self.statuses[node["id"]] = dict(instanceId=node["id"], version=node["version"], acceptingNewGames=False,
            targetVersion="", activeGames=0, reconnectingPlayers=0, connections=0, pendingWrites=0, safeToStop=False)
        self.events.append(("start", node["version"]))

    def healthy(self, node, require_closed=True):
        self.events.append(("healthy", node["version"]))
        if self.fail_health:
            raise RuntimeError("Candidato no disponible")

    def remove(self, node, staged=False):
        super().remove(node, staged)
        self.events.append(("remove", node["version"], staged))
        self.containers.pop(node["name"], None)

    def publish(self, data):
        super().publish(data)
        self.events.append(("routes", data["phase"], data["active"]["version"]))


class RollingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.state = Path(self.temporary.name); self.subject = FakeUpdater(self.state)
        self.commands = patch.object(updater, "command", return_value="created-id").start()
        patch.object(updater, "log").start()
        patch.object(updater, "inspect", side_effect=self.subject.lookup).start()
        self.http = patch.object(updater, "get_json", return_value=dict(tag_name="v0.29.66", draft=False, prerelease=False)).start()
        self.addCleanup(patch.stopall)

    def routes(self):
        return json.loads((self.subject.public / "routes.json").read_text())

    def update(self):
        self.subject.update(self.subject.data, "v0.29.66")
        return self.subject.data

    def test_update_keeps_started_games_and_routes_new_ones_to_candidate(self):
        original = self.subject.data["active"]; data = self.update()
        self.assertNotEqual(data["active"]["id"], original["id"])
        self.assertEqual(data["retiring"], [original])
        self.assertEqual(self.subject.statuses[original["id"]]["activeGames"], 2)
        self.assertFalse(self.subject.statuses[original["id"]]["acceptingNewGames"])
        self.assertTrue(self.subject.statuses[data["active"]["id"]]["acceptingNewGames"])
        self.assertEqual(self.routes()["active"], data["active"]["id"])
        self.assertEqual(len(self.routes()["nodes"]), 2)
        self.assertFalse(any(e[0] == "remove" for e in self.subject.events))

    def test_admission_is_closed_before_fetch_build_or_candidate_start(self):
        self.update(); events = self.subject.events
        self.assertLess(events.index(("routes", "starting", "0.29.65")), events.index(("admit", "0.29.65", False)))
        self.assertLess(events.index(("admit", "0.29.65", False)), events.index(("fetch", "v0.29.66")))
        self.assertLess(events.index(("healthy", "0.29.66")), events.index(("admit", "0.29.66", True)))
        self.assertLess(events.index(("admit", "0.29.66", True)), events.index(("routes", "ready", "0.29.66")))

    def test_build_failure_reopens_old_server_and_keeps_games(self):
        self.subject.fail_build = True
        with self.assertRaises(RuntimeError): self.update()
        self.assertEqual(self.subject.data["active"]["version"], "0.29.65")
        self.assertTrue(self.subject.statuses[self.subject.data["active"]["id"]]["acceptingNewGames"])
        self.assertEqual(self.routes()["phase"], "ready")
        self.assertFalse(any(e[0] == "remove" for e in self.subject.events))

    def test_unhealthy_candidate_is_removed_without_replacing_old(self):
        self.subject.fail_health = True
        with self.assertRaises(RuntimeError): self.update()
        self.assertIn(("remove", "0.29.66", True), self.subject.events)
        self.assertNotIn(("remove", "0.29.65", True), self.subject.events)
        self.assertEqual(self.subject.data["retiring"], [])
        self.assertEqual(self.routes()["active"], self.subject.data["active"]["id"])

    def test_reaper_waits_for_active_games_even_if_safe_flag_is_incoherent(self):
        data = self.update(); old = data["retiring"][0]
        self.subject.statuses[old["id"]].update(activeGames=1, safeToStop=True)
        self.subject.reap(data); self.assertEqual(len(data["retiring"]), 1)
        self.commands.assert_not_called()

    def test_pending_statistics_keep_finished_generation_alive(self):
        data = self.update(); old = data["retiring"][0]
        self.subject.statuses[old["id"]].update(activeGames=0, pendingWrites=1, safeToStop=True)
        self.subject.reap(data); self.assertEqual(len(data["retiring"]), 1)
        self.commands.assert_not_called()

    def test_final_result_grace_prevents_early_shutdown(self):
        data = self.update(); old = data["retiring"][0]
        self.subject.statuses[old["id"]].update(activeGames=0, pendingWrites=0, safeToStop=False)
        self.subject.reap(data); self.assertEqual(len(data["retiring"]), 1)

    def test_finished_generation_is_stopped_removed_and_unrouted_without_deleting_volumes(self):
        data = self.update(); old = data["retiring"][0]
        self.subject.statuses[old["id"]].update(activeGames=0, pendingWrites=0, safeToStop=True)
        self.subject.reap(data)
        self.assertEqual(data["retiring"], [])
        self.assertEqual(len(self.routes()["nodes"]), 1)
        self.assertEqual(self.commands.call_args_list[0].args[0], ["docker", "stop", "--time", "30", old["container_id"]])
        self.assertEqual(self.commands.call_args_list[1].args[0], ["docker", "rm", old["container_id"]])
        self.assertNotIn("--volumes", str(self.commands.call_args_list))
        self.assertEqual(self.subject.data["active"]["version"], "0.29.66")

    def test_reaper_rechecks_occupancy_immediately_before_stopping(self):
        data = self.update(); old = data["retiring"][0]
        safe = self.subject.lifecycle(old) | dict(activeGames=0, safeToStop=True)
        self.subject.statuses[old["id"]] = [safe, safe | dict(activeGames=1, safeToStop=False)]
        with self.assertRaises(RuntimeError): self.subject.reap(data)
        self.assertEqual(len(data["retiring"]), 1); self.commands.assert_not_called()

    def test_missing_retired_container_is_unrouted_without_touching_other_containers(self):
        data = self.update(); old = data["retiring"][0]
        self.subject.containers.pop(old["name"])
        self.subject.reap(data); self.assertEqual(data["retiring"], [])
        self.commands.assert_not_called()

    def test_foreign_container_with_reused_name_is_never_removed(self):
        data = self.update(); old = data["retiring"][0]
        self.subject.containers[old["name"]]["Id"] = "foreign"
        with self.assertRaises(RuntimeError): self.subject.reap(data)
        self.commands.assert_not_called()

    def test_ownership_label_is_required_even_when_container_id_matches(self):
        data = self.update(); old = data["retiring"][0]
        self.subject.containers[old["name"]]["Config"]["Labels"].clear()
        with self.assertRaises(RuntimeError): self.subject.reap(data)
        self.commands.assert_not_called()

    def test_another_release_waits_without_a_third_game_container(self):
        data = self.update(); before = len(self.subject.containers)
        self.subject.events.clear(); self.subject.update(data, "v0.29.67")
        self.assertEqual(data["queued_tag"], "v0.29.67")
        self.assertEqual(len(self.subject.containers), before)
        self.assertFalse(any(e[0] in ("fetch", "build", "start") for e in self.subject.events))

    def test_equal_or_older_release_does_not_restart(self):
        for tag in ("v0.29.65", "v0.29.64"):
            self.subject.update(self.subject.data, tag)
        self.assertEqual(self.subject.events, [])

    def test_recorded_applied_version_prevents_automatic_downgrade_after_manual_rollback(self):
        self.subject.data["applied_tag"] = "v0.29.67"
        self.subject.update(self.subject.data, "v0.29.66"); self.assertEqual(self.subject.events, [])

    def test_restart_during_build_reopens_existing_server(self):
        data = self.subject.data
        data.update(phase="starting", target="0.29.66"); self.subject.save(data); self.subject.publish(data)
        self.subject.reconcile(self.subject.load())
        self.assertEqual(self.routes()["phase"], "ready")
        self.assertEqual(self.routes()["active"], data["active"]["id"])

    def test_restart_after_create_recovers_candidate_identity_and_promotes(self):
        data = self.subject.data
        candidate = self.subject.node("0.29.66", "server:new", "new-commit", [data["active"]["port"]])
        self.subject.create_node(candidate); candidate.pop("container_id")
        data.update(phase="starting", target="0.29.66", candidate=candidate)
        self.subject.save(data); self.subject.publish(data)
        self.subject.reconcile(self.subject.load())
        saved = self.subject.load()
        self.assertEqual(saved["active"]["id"], candidate["id"])
        self.assertEqual(saved["active"]["container_id"], "container-" + candidate["id"])
        self.assertEqual(saved["retiring"][0]["version"], "0.29.65")

    def test_crash_after_switching_private_state_repairs_public_routes(self):
        data = self.update(); expected = self.routes()
        updater.write_json(self.subject.public / "routes.json", expected | dict(phase="starting"), public=True)
        self.subject.reconcile(data); self.assertEqual(self.routes(), expected)

    def test_failure_after_publication_never_destroys_new_games(self):
        real_publish = self.subject.publish
        def fail_after(data):
            real_publish(data)
            if data["active"]["version"] == "0.29.66": raise OSError("fsync interrumpido")
        self.subject.publish = fail_after
        with self.assertRaises(OSError): self.update()
        self.assertEqual(self.subject.load()["active"]["version"], "0.29.66")
        self.assertFalse(any(e[0] == "remove" for e in self.subject.events))

    def test_cleanup_intent_survives_failure_to_remove_staged_candidate(self):
        self.subject.fail_health = True
        real_remove = self.subject.remove
        self.subject.remove = lambda *a, **k: (_ for _ in ()).throw(OSError("Docker ocupado"))
        with self.assertRaises(OSError): self.update()
        data = self.subject.load()
        self.assertEqual(data["phase"], "ready"); self.assertTrue(data["retiring"][0]["staged"])
        self.assertEqual(len(self.routes()["nodes"]), 1)
        self.subject.remove = real_remove; self.subject.reap(data)
        self.assertEqual(self.subject.load()["retiring"], [])

    def test_stopped_active_generation_is_not_started_automatically(self):
        active = self.subject.data["active"]
        self.subject.containers[active["name"]]["State"]["Running"] = False
        self.subject.run(force_check=True)
        self.assertEqual(self.subject.events, []); self.http.assert_not_called(); self.commands.assert_not_called()

    def test_stopped_gateway_is_not_started_automatically(self):
        self.subject.containers[self.subject.gateway]["State"]["Running"] = False
        self.subject.run(force_check=True); self.assertEqual(self.subject.events, [])

    def test_missing_or_replaced_gateway_is_not_managed(self):
        self.subject.containers[self.subject.gateway]["Id"] = "another-gateway"
        with self.assertRaises(RuntimeError): self.subject.run()
        self.commands.assert_not_called()

    def test_github_outage_does_not_prevent_retiring_a_finished_generation(self):
        data = self.update(); old = data["retiring"][0]
        self.subject.statuses[old["id"]].update(activeGames=0, pendingWrites=0, safeToStop=True)
        self.http.side_effect = OSError("GitHub caído")
        with self.assertRaises(OSError): self.subject.run(force_check=True)
        self.assertEqual(self.subject.load()["retiring"], [])
        self.assertEqual(self.subject.load()["active"]["version"], "0.29.66")

    def test_release_check_waits_thirty_minutes_but_cleanup_still_runs(self):
        self.subject.data["next_check"] = updater.time.time() + 1000
        self.subject.save(self.subject.data); self.subject.run()
        self.http.assert_not_called()

    def test_public_routes_do_not_include_credentials_or_container_configuration(self):
        self.update(); text = (self.subject.public / "routes.json").read_text()
        self.assertNotIn(self.subject.config["management_token"], text)
        self.assertNotIn("private-value", text); self.assertNotIn("Mounts", text)

    def test_private_files_are_owner_only_and_proxy_routes_are_readable(self):
        self.assertEqual(self.subject.file.stat().st_mode & 0o777, 0o600)
        self.assertEqual((self.subject.public / "routes.json").stat().st_mode & 0o777, 0o644)

    def test_unchanged_routes_are_not_rewritten_every_five_seconds(self):
        file = self.subject.public / "routes.json"; before = file.stat().st_mtime_ns
        self.subject.publish(self.subject.data); self.assertEqual(file.stat().st_mtime_ns, before)

    def test_corrupt_generation_ids_ports_and_duplicate_nodes_are_rejected(self):
        original = copy.deepcopy(self.subject.data)
        for key, value in (("id", "bad"), ("port", 27015), ("name", "unrelated")):
            bad = copy.deepcopy(original); bad["active"][key] = value; self.subject.save(bad)
            with self.assertRaises(RuntimeError): self.subject.load()
        bad = copy.deepcopy(original); bad["retiring"] = [copy.deepcopy(bad["active"])]
        self.subject.save(bad)
        with self.assertRaises(RuntimeError): self.subject.load()

    def test_real_lifecycle_validation_rejects_negative_boolean_and_wrong_instance_counts(self):
        node = self.subject.data["active"]; valid = self.subject.lifecycle(node)
        for change in ({"activeGames": -1}, {"pendingWrites": True}, {"safeToStop": 1}, {"instanceId": "foreign"}, {"version": "0.29.64"}):
            self.http.return_value = valid | change
            with self.assertRaises(RuntimeError): updater.RollingUpdater.lifecycle(self.subject, node)

    def test_real_admission_is_persisted_before_request_and_checked(self):
        node = self.subject.data["active"]
        def response(url, data, secret):
            persisted = json.loads((self.state / "nodes" / node["id"] / "admission.json").read_text())
            self.assertEqual(persisted, data); self.assertEqual(secret, self.subject.config["management_token"])
            return self.subject.lifecycle(node) | dict(acceptingNewGames=False)
        self.http.side_effect = response
        updater.RollingUpdater.admission(self.subject, node, False, "0.29.66")
        self.http.side_effect = None; self.http.return_value = dict(instanceId="foreign", version=node["version"], acceptingNewGames=True)
        with self.assertRaises(RuntimeError): updater.RollingUpdater.admission(self.subject, node, True)

    def test_cloned_configuration_preserves_mounts_limits_and_credentials_without_leaking_them_in_arguments(self):
        node = self.subject.node("0.29.66", "server:new", "commit", [27017])
        observed = {}
        def command(args, capture=False, **kwargs):
            if args[1] == "create":
                file = Path(args[args.index("--env-file") + 1])
                observed.update(arguments=args, environment=dict(l.split("=", 1) for l in file.read_text().splitlines()), file=file)
                return "new-container-id"
            return ""
        self.commands.side_effect = command
        updater.RollingUpdater.create_node(self.subject, node)
        args = observed["arguments"]; env = observed["environment"]
        self.assertEqual(env["FIREBASE_API_KEY"], "private-value")
        self.assertEqual(env["ARCANA_LOOPBACK_ONLY"], "true"); self.assertEqual(env["ARCANA_DISCOVERY_ENABLED"], "false")
        self.assertIn("type=volume,src=original-data,dst=/app/data", args)
        self.assertIn("type=bind,src=/etc/arcana/firebase.json,dst=/app/firebase.json,readonly", args)
        self.assertIn("--memory", args); self.assertIn("--cpus", args)
        self.assertNotIn("private-value", str(args)); self.assertFalse(observed["file"].exists())

    def test_old_configuration_requires_migration_instead_of_recreating_a_running_server(self):
        with self.assertRaises(RuntimeError): updater.RollingUpdater({"checkout": "/tmp"}, self.state)

    def test_result_queues_must_be_on_shared_writable_mounts(self):
        config = copy.deepcopy(self.subject.config)
        config["template"]["Mounts"] = []
        with self.assertRaises(RuntimeError): updater.RollingUpdater(config, self.state)
        config = copy.deepcopy(self.subject.config)
        config["template"]["Mounts"][0]["RW"] = False
        with self.assertRaises(RuntimeError): updater.RollingUpdater(config, self.state)

    def test_bridge_network_and_private_publication_preserve_docker_dns(self):
        config = self.subject.config["template"]
        config["HostConfig"]["NetworkMode"] = "original_default"
        config["NetworkSettings"] = {"Networks": {"original_default": {"Gateway": "172.19.0.1"}}}
        node = self.subject.node("0.29.66", "server:new", "commit", [27017]); captured = {}
        def record(args, capture=False, **kwargs):
            if args[1] == "create":
                captured["args"] = args
                file = Path(args[args.index("--env-file") + 1])
                captured["env"] = dict(line.split("=", 1) for line in file.read_text().splitlines())
                return "bridge-node"
            return ""
        self.commands.side_effect = record
        updater.RollingUpdater.create_node(self.subject, node)
        self.assertIn("original_default", captured["args"])
        self.assertIn("127.0.0.1:27019:27019/tcp", captured["args"])
        self.assertEqual(captured["env"]["ARCANA_MANAGEMENT_PEER"], "172.19.0.1")
        self.assertEqual(captured["env"]["ARCANA_LOOPBACK_ONLY"], "false")

    def test_interrupted_initial_setup_is_recovered_without_replacing_first_generation(self):
        self.subject.config["template"]["Id"] = "legacy-stopped"
        self.subject.file.unlink(); self.subject.containers.clear(); self.subject.statuses.clear()
        def docker(args, capture=False, **kwargs):
            if args[1] == "create":
                gateway = dict(Id="created-gateway", State={"Running": False}, Config={"Labels": {
                    updater.OWNER: self.subject.config["project"], updater.OWNER + ".role": "gateway"}})
                self.subject.containers[self.subject.gateway] = gateway
                return gateway["Id"]
            if args[1] == "start": self.subject.lookup(args[2])["State"]["Running"] = True
            return ""
        self.commands.side_effect = docker
        self.http.side_effect = OSError("Proxy todavía no disponible")
        with patch.object(updater.time, "sleep"):
            with self.assertRaises(RuntimeError): self.subject.bootstrap("server:initial", "commit", "0.29.65")
        initial = self.subject.load(); self.assertTrue(initial["bootstrap"])
        original_id = initial["active"]["id"]
        self.http.side_effect = lambda *a, **k: dict(role="gateway", instanceId=original_id)
        self.subject.bootstrap("server:initial", "commit", "0.29.65")
        final = self.subject.load(); self.assertFalse(final["bootstrap"])
        self.assertEqual(final["active"]["id"], original_id)
        self.assertEqual(sum(e[0] == "start" for e in self.subject.events), 1)
        self.assertFalse(any(e[0] == "remove" for e in self.subject.events))

    def test_stopped_compose_container_recovers_gateway_from_network_metadata(self):
        config = self.subject.config["template"]
        config["HostConfig"]["NetworkMode"] = "original_default"
        config["NetworkSettings"] = {"Networks": {"original_default": {"Gateway": ""}}}
        node = self.subject.node("0.29.66", "server:new", "commit", [27017]); environment = {}
        def docker(args, capture=False, **kwargs):
            if args[1:3] == ["network", "inspect"]:
                return json.dumps([{"IPAM": {"Config": [{"Gateway": "172.20.0.1"}]}}])
            if args[1] == "create":
                environment.update(dict(line.split("=", 1) for line in Path(args[args.index("--env-file") + 1]).read_text().splitlines()))
                return "created-id"
            return ""
        self.commands.side_effect = docker
        updater.RollingUpdater.create_node(self.subject, node)
        self.assertEqual(environment["ARCANA_MANAGEMENT_PEER"], "172.20.0.1")

    def test_only_stable_numeric_release_tags_are_accepted(self):
        valid = dict(tag_name="v0.29.66", draft=False, prerelease=False)
        self.assertEqual(updater.stable_tag(valid), "v0.29.66")
        for change in ({"draft": True}, {"prerelease": True}, {"tag_name": "main"}, {"tag_name": "v0.29.66-prueba.1"}, {"tag_name": "--bad"}):
            with self.assertRaises(RuntimeError): updater.stable_tag(valid | change)

    def test_numeric_version_order_does_not_confuse_patch_nine_and_ten(self):
        self.assertGreater(updater.version("v0.29.10"), updater.version("0.29.9"))
        with self.assertRaises(RuntimeError): updater.version("main")


if __name__ == "__main__":
    unittest.main()
