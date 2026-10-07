#!/usr/bin/env python3
import importlib.util
import os
from pathlib import Path
import subprocess
import shutil
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('version_support', ROOT / 'scripts/version-support.py')
support = importlib.util.module_from_spec(spec)
spec.loader.exec_module(support)


class VersionSupportTests(unittest.TestCase):
    def setUp(self):
        self.presets = (ROOT / 'src/Arcana.Client/export_presets.cfg').read_text()

    def test_stale_presets_and_next_version(self):
        updated = support.update_presets(self.presets, support.parse_version('0.13.0'))
        for line in ('application/file_version="0.13.0.0"', 'application/product_version="0.13.0.0"',
                     'version/name="0.13.0"', 'version/code=13000',
                     'export_path="../../artifacts/android/ArcanaArena-v0.13.0-Android-arm64.apk"'):
            self.assertIn(line, updated)
        self.assertIn('package/unique_name="com.arcana.arena"', updated)

    def test_idempotence_and_crlf(self):
        crlf = self.presets.replace('\n', '\r\n')
        version = support.parse_version('0.12.19\r\n')
        updated = support.update_presets(crlf, version)
        self.assertEqual(support.update_presets(updated, version), updated)
        self.assertNotIn('\n', updated.replace('\r\n', ''))

    def test_increasing_code_and_limits(self):
        versions = ('0.12.14', '0.12.18', '0.12.19', '0.12.999', '0.13.0', '1.0.0', '2100.0.0')
        codes = [support.parse_version(v)['android_code'] for v in versions]
        self.assertEqual(codes, sorted(set(codes)))
        self.assertEqual(codes[-1], 2100000000)

    def test_invalid_versions(self):
        for value in ('', '0.12', '0.12.19-dev', '00.12.19', '0.0.0', '0.12.1000', '2100.0.1', '-1.2.3'):
            with self.subTest(value=value), self.assertRaises(ValueError):
                support.parse_version(value)

    def test_missing_or_duplicate_metadata(self):
        for key in ('application/file_version', 'application/product_version', 'version/name', 'version/code'):
            import re
            missing = re.sub('^' + re.escape(key) + r'=[^\r\n]*', '', self.presets, flags=re.M)
            for malformed in (missing, self.presets + '\n' + key + '=duplicado'):
                with self.subTest(key=key), self.assertRaises(ValueError):
                    support.update_presets(malformed, support.parse_version('0.12.19'))

    def test_cli_sync_preserves_other_files_and_check_rejects_stale(self):
        with tempfile.TemporaryDirectory(prefix='Arcana versiones [prueba] ') as folder:
            root = Path(folder)
            path = root / 'src/Arcana.Client/export_presets.cfg'
            path.parent.mkdir(parents=True)
            path.write_text(self.presets)
            (root / 'VERSION').write_text('0.13.0\n')
            command = ['python3', str(ROOT / 'scripts/version-support.py')]
            self.assertNotEqual(subprocess.run(command + ['check', folder], capture_output=True).returncode, 0)
            subprocess.run(command + ['sync', folder], check=True)
            subprocess.run(command + ['check', folder], check=True, capture_output=True)
            self.assertEqual((root / 'VERSION').read_text(), '0.13.0\n')

    @unittest.skipUnless(os.environ.get('ARCANA_TEST_DOTNET') or shutil.which('dotnet'), 'Requiere SDK .NET')
    def test_msbuild_reads_version_from_file(self):
        with tempfile.TemporaryDirectory(prefix="Arcana MSBuild O'Connor [prueba] ") as folder:
            root = Path(folder)
            (root / 'Directory.Build.props').write_bytes((ROOT / 'Directory.Build.props').read_bytes())
            (root / 'VERSION').write_text('0.13.0\n')
            project = root / 'Fixture.csproj'
            project.write_text('<Project Sdk="Microsoft.NET.Sdk" />\n')
            command = [os.environ.get('ARCANA_TEST_DOTNET', 'dotnet'),
                       os.environ.get('ARCANA_TEST_MSBUILD_DLL', 'msbuild'), str(project), '-nologo', '-getProperty:Version']
            result = subprocess.run(command, text=True, capture_output=True, check=True)
            self.assertEqual(result.stdout.strip(), '0.13.0')

    @unittest.skipUnless(os.environ.get('ARCANA_TEST_PWSH'), 'Comparacion opcional con PowerShell')
    def test_powershell_matches_python(self):
        with tempfile.TemporaryDirectory(prefix='Arcana versiones [prueba] ') as folder:
            root = Path(folder)
            source = root / 'presets.cfg'
            output = root / 'actual.cfg'
            source.write_text(self.presets.replace('\n', '\r\n'), newline='')
            script = root / 'compare.ps1'
            script.write_text('param($Helper,$Root,$Source,$Output)\n'
                              '. $Helper\n$v=Get-ArcanaBuildVersion -RepositoryRoot $Root\n'
                              '$text=Update-ArcanaExportVersion -Presets ([IO.File]::ReadAllText($Source)) -Version $v\n'
                              '[IO.File]::WriteAllText($Output,$text,(New-Object System.Text.UTF8Encoding($false)))\n')
            for name in ('0.12.19', '0.13.0', '1.2.3'):
                (root / 'VERSION').write_text(name + '\n')
                subprocess.run([os.environ['ARCANA_TEST_PWSH'], '-NoLogo', '-NoProfile', '-NonInteractive', '-File',
                                str(script), str(ROOT / 'scripts/version-support.ps1'), folder, str(source), str(output)], check=True)
                self.assertEqual(output.read_bytes().decode(),
                                 support.update_presets(source.read_bytes().decode(), support.parse_version(name)))


if __name__ == '__main__':
    unittest.main()
