#!/usr/bin/env python3
import argparse
import hashlib
import json
import os
import struct
import tempfile
import unittest
import zipfile
from pathlib import Path
from update_deltas import android_patch, apply_android, digest_file, safe_path, windows_patch


def archive(path, files, compression=zipfile.ZIP_DEFLATED):
    with zipfile.ZipFile(path, "w", compression, compresslevel=6) as output:
        for name, data in files.items():
            output.writestr(name, data)


def manifest(files, version):
    return json.dumps({"version": version, "files": [{"path": name, "length": len(data),
        "sha256": hashlib.sha256(data).hexdigest()} for name, data in files.items()]}).encode()


def fixtures(folder):
    folder.mkdir(parents=True, exist_ok=True)
    unchanged = os.urandom(1024 * 1024)
    old_files = {"ArcanaArena.exe": b"old-exe", "ArcanaArena.pck": b"old-pck",
        "server/Arcana.Server.exe": b"old-server", "runtime/Arcana.Client.dll": b"old-client",
        "runtime/coreclr.dll": unchanged, "VERSION.txt": b"0.17.16",
        "legacy.dll": b"obsolete-owned-file", "account-server.json": b"distributed-settings"}
    new_files = {**old_files, "ArcanaArena.exe": b"new-exe", "ArcanaArena.pck": b"new-pck",
        "runtime/Arcana.Client.dll": b"new-client", "VERSION.txt": b"0.17.17"}
    del new_files["legacy.dll"]
    old_files["UPDATE-MANIFEST.json"] = manifest(old_files, "0.17.16")
    new_files["UPDATE-MANIFEST.json"] = manifest(new_files, "0.17.17")
    archive(folder / "base-windows.zip", old_files)
    archive(folder / "target-windows.zip", new_files)
    windows_patch(folder / "base-windows.zip", folder / "target-windows.zip", "0.17.16", "0.17.17", folder / "windows-delta.zip")
    # APK-like ZIP: mover offsets, sustituir código y preservar bytes de entradas y firma.
    base_apk = {"classes.dex": b"old-code", "assets/shared.bin": unchanged,
        "assets/large.pck": unchanged + unchanged, "META-INF/signature": b"old-signature"}
    target_apk = {"classes.dex": b"new-code-that-moves-all-offsets", "assets/shared.bin": unchanged,
        "assets/large.pck": unchanged[:500000] + b"inserted-content" + unchanged[500000:] + unchanged,
        "META-INF/signature": b"target-signature-exact-bytes"}
    archive(folder / "base.apk", base_apk, zipfile.ZIP_STORED)
    archive(folder / "target.apk", target_apk, zipfile.ZIP_STORED)
    result = android_patch(folder / "base.apk", folder / "target.apk", "0.17.16", "0.17.17", folder / "android.delta")
    (folder / "android-result.json").write_text(json.dumps({"sha256": digest_file(folder / "target.apk").hex(),
        "length": (folder / "target.apk").stat().st_size, **result}))
    return result


class UpdateDeltaTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="Arcana delta O'Connor ")
        self.root = Path(self.temp.name)
        self.result = fixtures(self.root)

    def tearDown(self):
        self.temp.cleanup()

    def test_android_exact_reconstruction_after_offsets_and_insertions(self):
        output = self.root / "rebuilt.apk"
        apply_android(self.root / "base.apk", self.root / "android.delta", output, "0.17.16", "0.17.17")
        self.assertEqual(output.read_bytes(), (self.root / "target.apk").read_bytes())
        self.assertLess(self.result["bytes"], self.result["fullBytes"] * 0.3)
        self.assertGreater(self.result["reusedBytes"], self.result["fullBytes"] * 0.7)

    def test_android_changed_base_is_rejected(self):
        base = self.root / "base.apk"
        with base.open("r+b") as stream:
            stream.seek(80); stream.write(b"changed")
        with self.assertRaises(ValueError):
            apply_android(base, self.root / "android.delta", self.root / "bad.apk", "0.17.16", "0.17.17")

    def test_android_wrong_versions_and_truncation_are_rejected(self):
        with self.assertRaises(ValueError):
            apply_android(self.root / "base.apk", self.root / "android.delta", self.root / "bad.apk", "0.17.15", "0.17.17")
        patch = self.root / "android.delta"
        patch.write_bytes(patch.read_bytes()[:30])
        with self.assertRaises((ValueError, EOFError)):
            apply_android(self.root / "base.apk", patch, self.root / "bad.apk", "0.17.16", "0.17.17")

    def test_windows_does_not_download_unchanged_runtime(self):
        with zipfile.ZipFile(self.root / "windows-delta.zip") as patch:
            self.assertNotIn("runtime/coreclr.dll", patch.namelist())
            self.assertIn("account-server.json", patch.namelist())
            delta = json.loads(patch.read("UPDATE-DELTA.json"))
            self.assertEqual(delta["removed"][0]["path"], "legacy.dll")
            self.assertEqual(delta["manifestSha256"], hashlib.sha256(patch.read("UPDATE-MANIFEST.json")).hexdigest())
        self.assertLess((self.root / "windows-delta.zip").stat().st_size,
            (self.root / "target-windows.zip").stat().st_size * 0.05)

    def test_unsafe_paths_and_wrong_direction_are_rejected(self):
        for name in ("../x", "C:/x", "runtime/../x", "NUL.dll", "x:stream", "/x"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                safe_path(name)
        with self.assertRaises(ValueError):
            windows_patch(self.root / "base-windows.zip", self.root / "target-windows.zip",
                "0.17.17", "0.17.16", self.root / "bad.zip")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixtures", type=Path)
    args = parser.parse_args()
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(UpdateDeltaTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    if not result.wasSuccessful():
        raise SystemExit(1)
    if args.fixtures:
        print("DELTA_FIXTURES:", fixtures(args.fixtures))
