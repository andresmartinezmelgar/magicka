#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('preview_release', Path(__file__).with_name('prepare-preview-release.py'))
preview = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preview)


class PreviewReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / 'VERSION').write_text('0.18.1\n')
        (self.root / 'CHANGELOG.md').write_text('# Cambios\n\n## 0.18.1 — Prueba\n\n- Tienda más clara.\n- Nuevos poderes.\n\n## 0.18.0\n- Cambio antiguo.\n')
        (self.root / 'FIRMA-ANDROID.txt').write_text('Tipo de firma: PERMANENTE\n')
        for platform, ext in [('Windows-x64', 'zip'), ('Android-arm64', 'apk')]:
            path = self.root / f'ArcanaArena-v0.18.1-{platform}.{ext}'
            path.write_bytes(b'paquete firmado de prueba')
            manifest = 'SHA256SUMS-Android.txt' if ext == 'apk' else 'SHA256SUMS-Windows.txt'
            (self.root / manifest).write_text(f'{preview.digest(path)}  {path.name}\n')

    def tearDown(self):
        self.temp.cleanup()

    def run_prepare(self, branch='feature/defensa-oleadas'):
        return preview.prepare(self.root, self.root, branch, 'a' * 40, 17, self.root / 'notes.md', self.root / 'tag.txt')

    def test_signed_packages_keep_their_identity(self):
        result = self.run_prepare()
        self.assertEqual(result['tag'], 'preview-v0.18.1-r17')
        self.assertEqual((self.root / 'notes.md').read_text().splitlines()[0], 'Arcana-Preview-Branch: feature/defensa-oleadas')
        self.assertEqual(len((self.root / 'SHA256SUMS.txt').read_text().splitlines()), 2)
        notes = (self.root / 'notes.md').read_text()
        self.assertIn('- Tienda más clara.', notes)
        self.assertNotIn('Cambio antiguo', notes)

    def test_exact_version_notes_and_missing_changelog(self):
        from release_notes import notes
        self.assertNotIn('Tienda', notes(self.root, '0.18.2'))
        (self.root / 'CHANGELOG.md').unlink()
        self.assertFalse(any(line.startswith('- ') for line in notes(self.root, '0.18.1').splitlines()))

    def test_main_cannot_publish_as_preview(self):
        with self.assertRaises(ValueError):
            self.run_prepare('main')

    def test_modified_or_unsigned_apk_is_rejected(self):
        (self.root / 'ArcanaArena-v0.18.1-Android-arm64.apk').write_bytes(b'alterado')
        with self.assertRaises(ValueError):
            self.run_prepare()
        apk = self.root / 'ArcanaArena-v0.18.1-Android-arm64.apk'
        apk.write_bytes(b'paquete firmado de prueba')
        (self.root / 'FIRMA-ANDROID.txt').write_text('Tipo de firma: TEMPORAL\n')
        with self.assertRaises(ValueError):
            self.run_prepare()

    def add_delta(self, platform, extension, manifest):
        path = self.root / f'ArcanaArena-v0.18.1-{platform}-from-v0.18.0.{extension}'
        path.write_bytes(b'parche diferencial verificado')
        with (self.root / manifest).open('a') as stream:
            stream.write(f'{preview.digest(path)}  {path.name}\n')
        return path

    def test_both_platform_deltas_are_in_combined_checksums(self):
        android = self.add_delta('Android-arm64', 'delta', 'SHA256SUMS-Android.txt')
        windows = self.add_delta('Windows-x64', 'zip', 'SHA256SUMS-Windows.txt')
        self.run_prepare()
        checksums = (self.root / 'SHA256SUMS.txt').read_text().splitlines()
        self.assertEqual(len(checksums), 4)
        for path in (android, windows):
            self.assertIn(f'{preview.digest(path)}  {path.name}', checksums)

    def test_changed_or_missing_delta_cannot_be_published(self):
        delta = self.add_delta('Android-arm64', 'delta', 'SHA256SUMS-Android.txt')
        delta.write_bytes(b'modified')
        with self.assertRaises(ValueError):
            self.run_prepare()
        delta.unlink()
        with self.assertRaises(ValueError):
            self.run_prepare()

    def test_unlisted_delta_and_duplicate_manifest_entry_are_rejected(self):
        delta = self.root / 'ArcanaArena-v0.18.1-Windows-x64-from-v0.18.0.zip'
        delta.write_bytes(b'unlisted')
        with self.assertRaises(ValueError):
            self.run_prepare()
        delta.unlink()
        manifest = self.root / 'SHA256SUMS-Windows.txt'
        manifest.write_text(manifest.read_text() * 2)
        with self.assertRaises(ValueError):
            self.run_prepare()


if __name__ == '__main__':
    unittest.main()
