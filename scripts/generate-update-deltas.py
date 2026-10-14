#!/usr/bin/env python3
"""Genera parches contra Releases verificadas; si faltan bases, publica el paquete completo."""
import argparse
import hashlib
import json
import os
import tempfile
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
from urllib.parse import urlparse
from update_deltas import MAX_BYTES, android_patch, apply_android, digest_file, version, windows_patch


def request(url, limit, token=None):
    headers = {"User-Agent": "ArcanaArena-DeltaPublisher/1.0"}
    if token:
        headers["Authorization"] = "Bearer " + token
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as response:
        result = response.read(limit + 1)
        if len(result) > limit:
            raise ValueError("Respuesta demasiado grande")
        return result


def asset_url(asset, repository, tag):
    url = asset["browser_download_url"]
    parsed = urlparse(url)
    if parsed.scheme != "https" or parsed.netloc != "github.com" or parsed.query or parsed.fragment or \
            parsed.path != f"/{repository}/releases/download/{tag}/{asset['name']}":
        raise ValueError("Asset ajeno al repositorio o versión")
    return url


def download_base(release, platform, repository, destination):
    old_version = release["tag_name"].removeprefix("v")
    suffix = "Android-arm64.apk" if platform == "android" else "Windows-x64.zip"
    name = f"ArcanaArena-v{old_version}-{suffix}"
    assets = release.get("assets", [])
    candidates = [a for a in assets if a["name"] == name]
    if len(candidates) != 1:
        return None
    asset = candidates[0]
    if not 0 < asset["size"] <= MAX_BYTES:
        return None
    checksum_name = "SHA256SUMS-Android.txt" if platform == "android" else "SHA256SUMS-Windows.txt"
    checksums = [a for a in assets if a["name"] == checksum_name] or [a for a in assets if a["name"] == "SHA256SUMS.txt"]
    if len(checksums) != 1:
        return None
    text = request(asset_url(checksums[0], repository, release["tag_name"]), 65536).decode("utf-8-sig")
    hashes = [line[:64] for line in text.splitlines() if len(line) >= 67 and line[64:66] in ("  ", " *") and line[66:] == name]
    if len(hashes) != 1:
        return None
    expected = bytes.fromhex(hashes[0])
    if len(expected) != 32:
        return None
    path = destination / name
    with urllib.request.urlopen(urllib.request.Request(asset_url(asset, repository, release["tag_name"]),
            headers={"User-Agent": "ArcanaArena-DeltaPublisher/1.0"}), timeout=90) as response, path.open("wb") as output:
        received = 0
        while data := response.read(1024 * 1024):
            received += len(data)
            if received > asset["size"]:
                raise ValueError("Base de tamaño incorrecto")
            output.write(data)
    if path.stat().st_size != asset["size"] or digest_file(path) != expected:
        path.unlink(missing_ok=True)
        raise ValueError("El SHA-256 de la base no coincide con su Release")
    return path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--platform", choices=("windows", "android"), required=True)
    parser.add_argument("--target", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--repository", default="andresmartinezmelgar/magicka")
    parser.add_argument("--base", type=Path)
    parser.add_argument("--base-version")
    args = parser.parse_args()
    target_version = version(args.version)
    suffix = "Android-arm64" if args.platform == "android" else "Windows-x64"
    extension = "apk" if args.platform == "android" else "zip"
    if args.target.name != f"ArcanaArena-v{args.version}-{suffix}.{extension}":
        raise ValueError("Nombre del paquete de destino incorrecto")
    args.output.mkdir(parents=True, exist_ok=True)
    patch_extension = "delta" if args.platform == "android" else "zip"
    for stale in args.output.glob(f"ArcanaArena-v{args.version}-{suffix}-from-v*.{patch_extension}"):
        stale.unlink()
    created, reports = [], []
    with tempfile.TemporaryDirectory(prefix="arcana-delta-bases-") as temporary:
        folder = Path(temporary)
        bases = []
        if args.base:
            if not args.base_version or version(args.base_version) >= target_version:
                raise ValueError("Versión base inválida")
            bases.append((args.base_version, args.base))
        else:
            try:
                releases = json.loads(request(f"https://api.github.com/repos/{args.repository}/releases?per_page=30",
                    8 * 1024 ** 2, os.environ.get("GH_TOKEN")))
                eligible = []
                seen = set()
                for release in releases:
                    try:
                        old = release["tag_name"].removeprefix("v")
                        parsed = version(old)
                    except (ValueError, KeyError):
                        continue
                    if parsed >= target_version or parsed in seen or release.get("draft") or release.get("prerelease"):
                        continue
                    seen.add(parsed)
                    eligible.append((parsed, old, release))
                for _, old, release in sorted(eligible, reverse=True):
                    if len(bases) == 3:
                        break
                    try:
                        base = download_base(release, args.platform, args.repository, folder)
                        if base:
                            bases.append((old, base))
                    except (OSError, ValueError, urllib.error.URLError) as error:
                        print(f"DELTA_BASE_SKIPPED {old}: {type(error).__name__}")
            except (OSError, ValueError, urllib.error.URLError) as error:
                print(f"DELTA_BASES_UNAVAILABLE: {type(error).__name__}; paquete completo disponible")
        for old, base in bases:
            output = args.output / f"ArcanaArena-v{args.version}-{suffix}-from-v{old}.{'delta' if args.platform == 'android' else 'zip'}"
            try:
                generate = android_patch if args.platform == "android" else windows_patch
                result = generate(base, args.target, old, args.version, output)
                if result["bytes"] >= result["fullBytes"] * 0.85:
                    output.unlink(missing_ok=True)
                    print(f"DELTA_NOT_SMALLER {old}")
                    continue
                if args.platform == "android":
                    rebuilt = folder / "verified.apk"
                    apply_android(base, output, rebuilt, old, args.version)
                    if digest_file(rebuilt) != digest_file(args.target):
                        raise ValueError("La reconstrucción de la APK no es exacta")
                    rebuilt.unlink()
                created.append(output)
                reports.append({"baseVersion": old, "targetVersion": args.version, **result})
                print(f"DELTA_CREATED {output.name}: {result['bytes']}/{result['fullBytes']} bytes")
            except (OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
                output.unlink(missing_ok=True)
                print(f"DELTA_SKIPPED {old}: {type(error).__name__}; paquete completo disponible")
    checksum = args.output / ("SHA256SUMS-Android.txt" if args.platform == "android" else "SHA256SUMS-Windows.txt")
    checksum.write_text("".join(f"{digest_file(path).hex()}  {path.name}\n" for path in [args.target, *created]), encoding="utf-8")
    (args.output / f"DELTAS-{suffix}.json").write_text(json.dumps(reports, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
