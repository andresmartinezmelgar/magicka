#!/usr/bin/env python3
"""Pruebas de transición sin Docker, nginx, red ni compilación."""
import json
from pathlib import Path
import tempfile
import unittest
from types import SimpleNamespace
from rolling_deploy import RollingDeployment


def write_json(path, value):
    path.write_text(json.dumps(value))


class RollingTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        root = Path(self.tmp.name)
        key = root / 'key'; key.write_text('k' * 64); key.chmod(0o600)
        proxy = root / 'upstream.conf'; proxy.write_text('default 127.0.0.1:27015;\n')
        self.events = []; self.fail_health = False
        self.old = {'container': 'a' * 64, 'version': '0.29.69', 'port': 27015}
        self.new = {'container': 'b' * 64, 'version': '0.29.70', 'port': 28015}
        self.states = {self.old['container']: {'connections': 2, 'reservations': 1, 'pendingDeliveries': True},
                       self.new['container']: {'connections': 0, 'reservations': 0, 'pendingDeliveries': False}}
        updater = SimpleNamespace(state=root, config={'rolling_proxy': str(proxy), 'deployment_key_file': str(key)},
            fetch_source=lambda tag: self.events.append(('fetch', tag)), build=lambda image, tag: self.events.append(('build', tag)),
            wait_healthy=self.healthy)
        self.subject = RollingDeployment(updater, lambda args, **kw: self.events.append(tuple(args)), None, write_json, lambda msg: None)
        self.subject.lifecycle = self.lifecycle
        self.subject.candidate = lambda *args: self.new.copy()
        self.subject.switch = lambda active, retiring: self.events.append(('switch', active['version'], len(retiring)))
        self.subject.remove = lambda item: self.events.append(('remove', item['version']))
        write_json(self.subject.file, {'active': self.old, 'retiring': []})

    def lifecycle(self, item, draining=None):
        if draining is not None: self.events.append(('drain', item['version'], draining))
        return {**self.states[item['container']], 'version': item['version'], 'draining': bool(draining)}

    def healthy(self, *args):
        self.events.append(('health', args[-1]))
        if self.fail_health: raise RuntimeError('candidate failed')

    def test_new_version_routes_only_after_health_and_drain(self):
        self.subject.run('v0.29.70')
        self.assertLess(self.events.index(('health', '0.29.70')), self.events.index(('drain', '0.29.69', True)))
        self.assertLess(self.events.index(('drain', '0.29.69', True)), self.events.index(('switch', '0.29.70', 1)))
        self.assertNotIn(('remove', '0.29.69'), self.events)
        state = json.loads(self.subject.file.read_text())
        self.assertEqual(state['active']['version'], '0.29.70'); self.assertEqual(len(state['retiring']), 1)

    def test_old_is_kept_for_reconnects_and_pending_receipts(self):
        self.subject.run('v0.29.70')
        self.states[self.old['container']]['connections'] = 0
        self.subject.run('v0.29.70')
        self.assertNotIn(('remove', '0.29.69'), self.events)
        self.states[self.old['container']]['reservations'] = 0
        self.subject.run('v0.29.70')
        self.assertNotIn(('remove', '0.29.69'), self.events)
        self.states[self.old['container']]['pendingDeliveries'] = False
        self.subject.run('v0.29.70')
        self.assertIn(('remove', '0.29.69'), self.events)

    def test_no_third_container_while_old_has_players(self):
        self.subject.run('v0.29.70'); self.events.clear(); self.subject.run('v0.29.71')
        self.assertFalse(any(e[0] == 'build' for e in self.events))

    def test_failed_candidate_never_drains_old_server(self):
        self.fail_health = True
        with self.assertRaises(RuntimeError): self.subject.run('v0.29.70')
        self.assertNotIn(('drain', '0.29.69', True), self.events)
        self.subject.recover(json.loads(self.subject.file.read_text()))
        self.assertIn(('remove', '0.29.70'), self.events)
        self.assertNotIn(('remove', '0.29.69'), self.events)

    def test_interrupted_switch_preserves_candidate_that_received_players(self):
        self.states[self.new['container']]['connections'] = 1
        state = self.subject.recover({'active': self.old, 'retiring': [], 'pending': {'candidate': self.new, 'phase': 'switching'}})
        self.assertEqual(state['active']['version'], '0.29.70')
        self.assertFalse(any(e[0] == 'remove' for e in self.events))

    def test_interrupted_switch_rolls_back_unused_candidate(self):
        state = self.subject.recover({'active': self.old, 'retiring': [], 'pending': {'candidate': self.new, 'phase': 'switching'}})
        self.assertEqual(state['active']['version'], '0.29.69')
        self.assertIn(('drain', '0.29.69', False), self.events)
        self.assertIn(('remove', '0.29.70'), self.events)

    def test_private_key_permissions_are_required(self):
        path = Path(self.subject.updater.config['deployment_key_file']); path.chmod(0o644)
        with self.assertRaises(RuntimeError):
            RollingDeployment(self.subject.updater, None, None, write_json, None)

    def test_interrupted_removal_does_not_require_health_of_stopped_instance(self):
        self.subject.run('v0.29.70')
        self.states[self.old['container']] = {'connections': 0, 'reservations': 0, 'pendingDeliveries': False}
        self.subject.remove = lambda item: (_ for _ in ()).throw(RuntimeError('cut after stop'))
        with self.assertRaises(RuntimeError): self.subject.run('v0.29.70')
        self.assertTrue(json.loads(self.subject.file.read_text())['retiring'][0]['removing'])
        self.subject.remove = lambda item: self.events.append(('remove', item['version']))
        original = self.subject.lifecycle
        def online_only(item, draining=None):
            if item['version'] == self.old['version']: raise RuntimeError('stopped')
            return original(item, draining)
        self.subject.lifecycle = online_only
        self.subject.run('v0.29.70')
        self.assertEqual(json.loads(self.subject.file.read_text())['retiring'], [])

    def candidate_commands(self, external=False):
        config = {'services': {'server': {'environment': {}, 'volumes': [{'type': 'volume', 'source': 'data', 'target': '/app/data'}]}},
                  'volumes': {'data': {'name': 'old-data', 'external': external}}, 'networks': {'default': {'name': 'old-network'}}}
        self.subject.updater.base_command = lambda: ['docker', 'compose']
        self.subject.updater.compose = ['docker', 'compose']
        def command(args, **kwargs):
            self.events.append(tuple(args))
            if args[-3:] == ['config', '--format', 'json']: return json.dumps(config)
            if args[:3] == ['docker', 'network', 'inspect']: return json.dumps([{'IPAM': {'Config': [{'Gateway': '172.17.0.1'}]}}])
            if args[-3:] == ['ps', '-q', 'server']: return 'b' * 64
            return ''
        self.subject.command = command

    def test_candidate_has_private_port_distinct_journal_and_exact_proxy(self):
        self.candidate_commands()
        item = RollingDeployment.candidate(self.subject, self.old, 'test-image', '0.29.70')
        config = json.loads(Path(item['config']).read_text()); server = config['services']['server']
        self.assertEqual(server['ports'][0]['host_ip'], '127.0.0.1')
        self.assertEqual(server['environment']['ARCANA_TRUSTED_PROXY_IP'], '172.17.0.1')
        self.assertEqual(server['network_mode'], 'bridge')
        self.assertNotIn('name', config['volumes']['data'])
        self.assertEqual(Path(item['config']).stat().st_mode & 0o777, 0o600)

    def test_shared_external_journals_are_rejected_before_starting_candidate(self):
        self.candidate_commands(external=True)
        with self.assertRaises(RuntimeError): RollingDeployment.candidate(self.subject, self.old, 'test-image', '0.29.70')
        self.assertFalse(any('up' in e for e in self.events))


if __name__ == '__main__': unittest.main()
