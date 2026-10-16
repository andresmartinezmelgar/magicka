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

    def test_main_cannot_publish_as_preview(self):
        with self.assertRaises(ValueError):
            self.run_prepare('main')

    def test_modified_or_unsigned_apk_is_rejected(self):
        (self.root / 'ArcanaArena-v0.18.1-Android-arm64.apk').write_bytes(b'alterado')
        with self.assertRaises(ValueError):
            self.run_prepare()
        (self.root / 'FIRMA-ANDROID.txt').write_text('Tipo de firma: TEMPORAL\n')
        with self.assertRaises(ValueError):
            self.run_prepare()


if __name__ == '__main__':
    unittest.main()
