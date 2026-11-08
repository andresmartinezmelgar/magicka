#!/usr/bin/env python3
"""Pruebas sin compilar ni sustituir ningún servidor real."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("raspberry_updater", Path(__file__).with_name("raspberry-auto-update.py"))
updater = importlib.util.module_from_spec(spec)
spec.loader.exec_module(updater)


class FakeUpdater(updater.Updater):
    def __init__(self, state):
        self.state = state
        self.events = []
        self.current = "0.29.50"
        self.humans = [0, 0]
        self.containers = [{"Id": "original", "Image": "sha256:previous", "State": {"Running": True}}] * 2
        self.fail_build = False
        self.fail_health = False

    def container(self):
        return self.containers.pop(0)

    def status(self, container):
        return self.current, self.humans.pop(0), "http://127.0.0.1:27015"

    def fetch_source(self, tag):
        self.events.append(("fetch", tag))
        return "release-commit"

    def build(self, image, tag):
        self.events.append(("build", image))
        if self.fail_build:
            raise RuntimeError("No hay espacio para compilar")

    def start(self, image):
        self.events.append(("start", image))

    def wait_healthy(self, base, expected):
        self.events.append(("healthy", expected))
        if self.fail_health and expected != self.current:
            raise RuntimeError("No arranca la nueva imagen")


class ReleaseUpdateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.state = Path(self.temporary.name)
        self.subject = FakeUpdater(self.state)
        self.release = {"tag_name": "v0.29.51", "draft": False, "prerelease": False}
        self.http = patch.object(updater, "get_json", return_value=self.release).start()
        self.commands = patch.object(updater, "command").start()
        self.addCleanup(patch.stopall)

    def test_stable_is_built_then_started_and_verified_before_recording(self):
        self.subject.run()
        self.assertEqual(self.subject.events, [("fetch", "v0.29.51"),
            ("build", "arcana-arena-server:auto-v0.29.51"),
            ("start", "arcana-arena-server:auto-v0.29.51"), ("healthy", "0.29.51")])
        self.commands.assert_called_once_with(["docker", "image", "tag", "sha256:previous", "arcana-arena-server:previous"])
        self.assertEqual(json.loads((self.state / "applied.json").read_text()),
                         {"tag": "v0.29.51", "commit": "release-commit", "previous_version": "0.29.50"})

    def test_same_release_does_not_build_or_restart(self):
        self.subject.current = "0.29.51"
        self.subject.run()
        self.assertEqual(self.subject.events, [])
        self.commands.assert_not_called()

    def test_no_automatic_downgrade_if_latest_is_changed_to_older_release(self):
        self.subject.current = "0.30.0"
        self.subject.run()
        self.assertEqual(self.subject.events, [])

    def test_applied_version_respects_explicit_manual_rollback(self):
        (self.state / "applied.json").write_text(json.dumps({"tag": "v0.29.51"}))
        self.subject.run()
        self.assertEqual(self.subject.events, [])

    def test_previews_drafts_and_invalid_tags_are_rejected(self):
        for change in ({"prerelease": True}, {"draft": True}, {"tag_name": "v0.29.51-prueba.1"},
                       {"tag_name": "main"}, {"tag_name": "--upload-pack=bad"}):
            with self.subTest(change=change):
                self.http.return_value = {**self.release, **change}
                with self.assertRaises(RuntimeError):
                    self.subject.run()
        self.assertEqual(self.subject.events, [])

    def test_github_failure_leaves_server_untouched(self):
        self.http.side_effect = OSError("Sin Internet")
        with self.assertRaises(OSError):
            self.subject.run()
        self.assertEqual(self.subject.events, [])

    def test_busy_server_defers_before_downloading(self):
        self.subject.humans = [2]
        self.subject.run()
        self.assertEqual(self.subject.events, [])

    def test_player_joining_during_build_defers_without_stopping(self):
        self.subject.humans = [0, 1]
        self.subject.run()
        self.assertEqual([event[0] for event in self.subject.events], ["fetch", "build"])
        self.commands.assert_not_called()

    def test_manual_instance_change_during_build_is_respected(self):
        self.subject.containers[1] = {"Id": "another"}
        self.subject.run()
        self.assertNotIn("start", [event[0] for event in self.subject.events])

    def test_stopped_server_is_not_restarted(self):
        self.subject.containers = [None]
        self.subject.run()
        self.assertEqual(self.subject.events, [])

    def test_failed_build_leaves_running_server_and_state_untouched(self):
        self.subject.fail_build = True
        with self.assertRaises(RuntimeError):
            self.subject.run()
        self.assertNotIn("start", [event[0] for event in self.subject.events])
        self.assertFalse((self.state / "applied.json").exists())

    def test_failed_health_restores_previous_image_without_marking_release_applied(self):
        self.subject.fail_health = True
        with self.assertRaisesRegex(RuntimeError, "Se restauró 0.29.50"):
            self.subject.run()
        self.assertEqual(self.subject.events[-2:], [("start", "arcana-arena-server:previous"), ("healthy", "0.29.50")])
        self.assertFalse((self.state / "applied.json").exists())


class ConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        with patch.object(updater, "compose_command", return_value=["docker", "compose"]):
            self.subject = updater.Updater({"checkout": str(self.root), "project": "magicka",
                "compose_files": [str(self.root / "compose.yaml"), str(self.root / "compose.private.yaml")]}, self.root)

    def test_release_source_preserves_original_project_configuration_and_volumes(self):
        arguments = self.subject.image_command("arcana-arena-server:auto-v0.29.51")
        self.assertEqual(arguments, ["docker", "compose", "--project-directory", str(self.root),
            "--project-name", "magicka", "-f", str(self.root / "compose.yaml"),
            "-f", str(self.root / "compose.private.yaml"), "-f", str(self.root / "compose.update.json")])
        override = json.loads(self.subject.override.read_text())["services"]["server"]
        self.assertEqual(override["build"]["context"], str(self.root / "source"))
        self.assertNotIn("volumes", override)
        self.assertNotIn("ports", override)

    def test_replacement_never_deletes_volumes_or_builds(self):
        with patch.object(updater, "command") as run:
            self.subject.start("arcana-arena-server:previous")
        arguments = run.call_args.args[0]
        self.assertEqual(arguments[-6:], ["up", "-d", "--no-build", "--no-deps", "--force-recreate", "server"])
        self.assertNotIn("down", arguments)
        self.assertNotIn("--volumes", arguments)

    def test_occupancy_ignores_bots_and_includes_all_rooms(self):
        container = {"Config": {"Env": ["GAME_PORT=28000", "BOTS=2"]}}
        directory = {"server": {"players": 6}, "rooms": [{"players": 3, "bots": 2}]}
        with patch.object(updater, "get_json", side_effect=[{"status": "ok", "version": "0.29.50"}, directory]) as http:
            self.assertEqual(self.subject.status(container), ("0.29.50", 2, "http://127.0.0.1:28000"))
        self.assertTrue(all(call.args[0].startswith("http://127.0.0.1:28000/") for call in http.call_args_list))

    def test_malformed_occupancy_never_assumes_empty(self):
        container = {"Config": {"Env": ["BOTS=0"]}}
        directory = {"server": {"players": "0"}, "rooms": []}
        with patch.object(updater, "get_json", side_effect=[{"status": "ok", "version": "0.29.50"}, directory]):
            with self.assertRaisesRegex(RuntimeError, "ocupación"):
                self.subject.status(container)

    def test_http_200_with_old_version_does_not_pass_health(self):
        with patch.object(updater, "get_json", return_value={"status": "ok", "version": "0.29.50"}), patch.object(updater.time, "sleep"):
            with self.assertRaisesRegex(RuntimeError, "0.29.51"):
                self.subject.wait_healthy("http://127.0.0.1:27015", "0.29.51")

    def test_installer_keeps_actual_compose_project_and_sets_persistent_half_hour_timer(self):
        base = self.root / "compose.yaml"
        private = self.root / "compose.private.yaml"
        base.touch()
        private.touch()
        units = self.root / "units"
        units.mkdir()
        configuration = self.root / "config.json"
        program = self.root / "program" / "update.py"
        labels = {"com.docker.compose.project": "actual-project",
                  "com.docker.compose.project.config_files": str(base) + "," + str(private)}
        def execute(arguments, **kwargs):
            return json.dumps([{"State": {"Running": True}, "Config": {"Labels": labels}}]) if arguments[:2] == ["docker", "inspect"] else "container-id"
        with patch.object(updater, "compose_command", return_value=["docker", "compose"]), \
             patch.object(updater, "command", side_effect=execute) as run, \
             patch.object(updater, "STATE", self.root / "state"), patch.object(updater, "CONFIG", configuration), \
             patch.object(updater, "PROGRAM", program), patch.object(updater, "UNITS", units):
            updater.install(self.root)
        self.assertEqual(json.loads(configuration.read_text())["project"], "actual-project")
        self.assertEqual(json.loads(configuration.read_text())["compose_files"], [str(base), str(private)])
        self.assertEqual(configuration.stat().st_mode & 0o777, 0o600)
        self.assertTrue(program.exists())
        timer = (units / "arcana-server-update.timer").read_text()
        self.assertIn("OnCalendar=*-*-* *:00,30:00", timer)
        self.assertIn("Persistent=true", timer)
        run.assert_any_call(["systemctl", "enable", "--now", "arcana-server-update.timer"])


class RealGitTests(unittest.TestCase):
    def test_exact_tag_version_validation_and_preservation_of_original_checkout(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            origin = root / "origin"
            origin.mkdir()
            def git(*args, cwd=origin):
                return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True, text=True).stdout.strip()
            git("init", "--initial-branch=main")
            git("config", "user.name", "Updater test")
            git("config", "user.email", "updater@example.invalid")
            (origin / "VERSION").write_text("0.29.51\n")
            git("add", "VERSION")
            git("commit", "-m", "Stable")
            commit = git("rev-parse", "HEAD")
            git("tag", "v0.29.51")
            git("tag", "v0.29.52")
            state = root / "state"
            state.mkdir()
            with patch.object(updater, "compose_command", return_value=["docker", "compose"]):
                subject = updater.Updater({"checkout": str(origin), "project": "magicka", "compose_files": []}, state)
            subject.source.mkdir()
            git("init", cwd=subject.source)
            git("remote", "add", "origin", str(origin), cwd=subject.source)
            (origin / "user-file").write_text("conservar")
            self.assertEqual(subject.fetch_source("v0.29.51"), commit)
            self.assertEqual((subject.source / "VERSION").read_text(), "0.29.51\n")
            with self.assertRaisesRegex(RuntimeError, "VERSION"):
                subject.fetch_source("v0.29.52")
            self.assertEqual((origin / "user-file").read_text(), "conservar")
            self.assertEqual(git("symbolic-ref", "--short", "HEAD"), "main")


if __name__ == "__main__":
    unittest.main()
