#!/usr/bin/env python3
"""Regresiones del canal y la identidad de las bases publicadas para los deltas."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('delta_publisher', Path(__file__).with_name('generate-update-deltas.py'))
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)
BRANCH = 'feature/defensa-oleadas'


def release(tag, branch=BRANCH, **changes):
    return dict(tag_name=tag, prerelease=tag.startswith('preview-'), draft=False,
                target_commitish='a' * 40, body=f'Arcana-Preview-Branch: {branch}\n', assets=[], **changes)


class DeltaPublisherTests(unittest.TestCase):
    def test_stable_and_preview_bases_are_isolated(self):
        releases = [release('v0.19.1'), release('preview-v0.19.1-r468'),
                    release('preview-v0.19.1-r467'), release('preview-v0.18.9-r465'),
                    release('preview-v0.19.1-r999', 'feature/other')]
        stable = publisher.eligible_releases(releases, (0, 19, 2))
        previews = publisher.eligible_releases(releases, (0, 19, 2), BRANCH)
        self.assertEqual([item[3]['tag_name'] for item in stable], ['v0.19.1'])
        self.assertEqual([item[3]['tag_name'] for item in previews],
                         ['preview-v0.19.1-r468', 'preview-v0.19.1-r467', 'preview-v0.18.9-r465'])

    def test_invalid_unpublished_and_non_older_previews_are_ignored(self):
        valid = release('preview-v0.19.1-r468')
        releases = [{**valid, **changes} for changes in [
            dict(draft=True), dict(prerelease=False), dict(target_commitish='main'),
            dict(target_commitish='refs/heads/main'), dict(target_commitish=''),
            dict(body=''), dict(body=valid['body'] * 2),
            dict(body='Arcana-Preview-Branch: main\n'),
            dict(tag_name='preview-v0.19.2-r469'), dict(tag_name='preview-v0.20.0-r470'),
            dict(tag_name='preview-v00.19.1-r468'), dict(tag_name='preview-v0.19.1-r0'),
            dict(tag_name='preview-v0.19.1-r468-extra')]]
        self.assertEqual(publisher.eligible_releases(releases, (0, 19, 2), BRANCH), [])
        for branch in ('main', 'refs/heads/main', '', 'feature/with space'):
            with self.subTest(branch=branch), self.assertRaises(ValueError):
                publisher.eligible_releases([valid], (0, 19, 2), branch)

    def test_latest_build_per_version_and_three_version_limit(self):
        releases = [release(tag) for tag in ['preview-v0.18.7-r462', 'preview-v0.19.1-r467',
                    'preview-v0.18.9-r465', 'preview-v0.19.1-r468', 'preview-v0.18.8-r464']]
        with patch.object(publisher, 'download_base', return_value=Path('verified-package')) as download:
            bases = publisher.published_bases(releases, (0, 19, 2), 'android', 'owner/repo', Path('.'), BRANCH)
        self.assertEqual([base[0] for base in bases], ['0.19.1', '0.18.9', '0.18.8'])
        self.assertEqual([call.args[0]['tag_name'] for call in download.call_args_list],
                         ['preview-v0.19.1-r468', 'preview-v0.18.9-r465', 'preview-v0.18.8-r464'])

    def test_unavailable_latest_build_can_use_verified_previous_build(self):
        releases = [release('preview-v0.19.1-r467'), release('preview-v0.19.1-r468')]
        with patch.object(publisher, 'download_base', side_effect=[ValueError('bad hash'), Path('verified')]) as download:
            bases = publisher.published_bases(releases, (0, 19, 2), 'windows', 'owner/repo', Path('.'), BRANCH)
        self.assertEqual(bases, [('0.19.1', Path('verified'))])
        self.assertEqual([call.args[0]['tag_name'] for call in download.call_args_list],
                         ['preview-v0.19.1-r468', 'preview-v0.19.1-r467'])

    def test_preview_asset_uses_package_version_and_rejects_tampering(self):
        source = release('preview-v0.19.1-r468')
        data = b'signed-package-exact-bytes'
        name = 'ArcanaArena-v0.19.1-Android-arm64.apk'
        url = f'https://github.com/owner/repo/releases/download/{source["tag_name"]}/'
        source['assets'] = [dict(name=name, size=len(data), browser_download_url=url + name),
                            dict(name='SHA256SUMS-Android.txt', browser_download_url=url + 'SHA256SUMS-Android.txt')]
        checksums = f'{hashlib.sha256(data).hexdigest()}  {name}\n'.encode()
        with tempfile.TemporaryDirectory() as folder, patch.object(publisher, 'request', return_value=checksums):
            with patch.object(publisher.urllib.request, 'urlopen', return_value=io.BytesIO(data)):
                base = publisher.download_base(source, 'android', 'owner/repo', Path(folder), '0.19.1')
                self.assertEqual(base.name, name)
                self.assertEqual(base.read_bytes(), data)
            with patch.object(publisher.urllib.request, 'urlopen', return_value=io.BytesIO(b'x' * len(data))):
                with self.assertRaises(ValueError):
                    publisher.download_base(source, 'android', 'owner/repo', Path(folder), '0.19.1')
                self.assertFalse(base.exists())

    def test_published_preview_builds_verified_patches_and_manifests_for_both_platforms(self):
        fixture_spec = importlib.util.spec_from_file_location('delta_fixtures', Path(__file__).with_name('test-update-deltas.py'))
        fixtures = importlib.util.module_from_spec(fixture_spec)
        fixture_spec.loader.exec_module(fixtures)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            fixtures.fixtures(root / 'sources')
            for platform, suffix, extension, base_file, target_file in [
                    ('android', 'Android-arm64', 'apk', 'base.apk', 'target.apk'),
                    ('windows', 'Windows-x64', 'zip', 'base-windows.zip', 'target-windows.zip')]:
                with self.subTest(platform=platform):
                    output = root / platform
                    output.mkdir()
                    target = output / f'ArcanaArena-v0.17.17-{suffix}.{extension}'
                    shutil.copyfile(root / 'sources' / target_file, target)
                    source = release('preview-v0.17.16-r1')
                    data = (root / 'sources' / base_file).read_bytes()
                    name = f'ArcanaArena-v0.17.16-{suffix}.{extension}'
                    checksum_name = f'SHA256SUMS-{"Android" if platform == "android" else "Windows"}.txt'
                    url = f'https://github.com/owner/repo/releases/download/{source["tag_name"]}/'
                    source['assets'] = [dict(name=name, size=len(data), browser_download_url=url + name),
                                        dict(name=checksum_name, browser_download_url=url + checksum_name)]
                    checksums = f'{hashlib.sha256(data).hexdigest()}  {name}\n'.encode()
                    def response(url, limit, token=None):
                        return json.dumps([source]).encode() if url.startswith('https://api.github.com/') else checksums
                    argv = ['generate-update-deltas.py', '--platform', platform, '--target', str(target),
                            '--version', '0.17.17', '--output', str(output), '--repository', 'owner/repo',
                            '--preview-branch', BRANCH]
                    with patch.object(sys, 'argv', argv), patch.object(publisher, 'request', side_effect=response), \
                            patch.object(publisher.urllib.request, 'urlopen', return_value=io.BytesIO(data)):
                        publisher.main()
                    delta_extension = 'delta' if platform == 'android' else 'zip'
                    delta = output / f'ArcanaArena-v0.17.17-{suffix}-from-v0.17.16.{delta_extension}'
                    self.assertTrue(delta.is_file())
                    self.assertLess(delta.stat().st_size, target.stat().st_size * 0.3)
                    report = json.loads((output / f'DELTAS-{suffix}.json').read_text())
                    self.assertEqual([entry['baseVersion'] for entry in report], ['0.17.16'])
                    self.assertEqual(report[0]['targetVersion'], '0.17.17')
                    self.assertIn(f'{publisher.digest_file(delta).hex()}  {delta.name}',
                                  (output / checksum_name).read_text().splitlines())


if __name__ == '__main__':
    unittest.main()
