"""Exercise the real RAR archiver and reject incomplete, corrupt or unsafe ZIP inputs."""
import argparse
import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('package_windows_rar', Path(__file__).with_name('package-windows-rar.py'))
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)
rar = 'rar'


class WindowsRarTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='arcana-rar-test-')
        self.root = Path(self.temporary.name)
        self.source = self.root / 'Windows.zip'
        self.output = self.root / 'Windows.rar'

    def tearDown(self):
        self.temporary.cleanup()

    def fixture(self, extra=None):
        entries = {'ArcanaArena.exe': b'MZ' + bytes(range(256)) * 128, 'VERSION.txt': b'0.29.28\n',
                   'server/Arcana.Core.dll': b'Arcana' * 8000, 'datos/nombre con espacios.txt': 'Integridad: áéñ'.encode()}
        entries.update(extra or {})
        with zipfile.ZipFile(self.source, 'w', compression=zipfile.ZIP_STORED) as archive:
            for name, contents in entries.items(): archive.writestr(name, contents)

    def test_real_archive_round_trip(self):
        self.fixture()
        packager.package(self.source, self.output, rar)
        self.assertEqual(self.output.read_bytes()[:8], b'Rar!\x1a\x07\x01\x00')

    def test_rejects_unsafe_paths_and_preserves_existing_output(self):
        for name in ['../outside.txt', '/absolute.txt', 'C:/outside.txt', 'folder\\outside.txt']:
            with self.subTest(name=name):
                self.fixture({name: b'unsafe'})
                self.output.write_bytes(b'previous package')
                with self.assertRaises(ValueError): packager.package(self.source, self.output, rar)
                self.assertEqual(self.output.read_bytes(), b'previous package')
                self.assertFalse((self.root / 'outside.txt').exists())

    def test_rejects_incomplete_package(self):
        with zipfile.ZipFile(self.source, 'w') as archive: archive.writestr('VERSION.txt', b'0.29.28')
        with self.assertRaises(ValueError): packager.package(self.source, self.output, rar)
        self.assertFalse(self.output.exists())

    def test_rejects_corrupt_zip(self):
        self.fixture()
        content = bytearray(self.source.read_bytes())
        content[content.index(b'MZ') + 1] ^= 0xff
        self.source.write_bytes(content)
        with self.assertRaises((ValueError, zipfile.BadZipFile)): packager.package(self.source, self.output, rar)
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--rar', default='rar')
    args, remaining = parser.parse_known_args()
    rar = args.rar
    unittest.main(argv=[__file__, *remaining])
