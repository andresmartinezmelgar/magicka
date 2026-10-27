#!/usr/bin/env python3
"""Prepara una publicación de pruebas con los mismos binarios firmados de CI."""
import argparse
import hashlib
import json
import re
from pathlib import Path
from release_notes import notes


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(chunk)
    return value.hexdigest()


def prepare(root, dist, branch, commit, build, notes_file, tag_file):
    version = (root / 'VERSION').read_text(encoding='utf-8-sig').strip()
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', version):
        raise ValueError('VERSION inválida.')
    if branch in ('main', 'refs/heads/main') or not 1 <= len(branch) <= 200 or any(c.isspace() or ord(c) < 32 for c in branch):
        raise ValueError('La publicación de pruebas exige una rama distinta de main.')
    if not re.fullmatch(r'[a-f0-9]{40}', commit) or not isinstance(build, int) or not 1 <= build < 10**18:
        raise ValueError('Identidad de compilación inválida.')
    signature = (dist / 'FIRMA-ANDROID.txt').read_text(encoding='utf-8')
    if 'Tipo de firma: PERMANENTE' not in signature.splitlines():
        raise ValueError('La APK de pruebas debe conservar el certificado permanente.')
    checksums = []
    for platform, extension in [('Windows-x64', 'zip'), ('Android-arm64', 'apk')]:
        name = f'ArcanaArena-v{version}-{platform}.{extension}'
        path = dist / name
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f'Falta el paquete completo {name}.')
        manifest = dist / ('SHA256SUMS-Android.txt' if extension == 'apk' else 'SHA256SUMS-Windows.txt')
        patch_extension = 'delta' if extension == 'apk' else 'zip'
        packages = [path, *sorted(dist.glob(f'ArcanaArena-v{version}-{platform}-from-v*.{patch_extension}'))]
        entries = {}
        for line in manifest.read_text(encoding='utf-8-sig').splitlines():
            match = re.fullmatch(r'([a-fA-F0-9]{64}) [ *]([^/\\]+)', line)
            if not match or match[2] in entries:
                raise ValueError(f'Manifiesto de integridad inválido: {manifest.name}.')
            entries[match[2]] = match[1].lower()
        if set(entries) != {package.name for package in packages}:
            raise ValueError(f'El manifiesto no coincide con los paquetes y deltas de {platform}.')
        for package in packages:
            if not package.is_file() or package.stat().st_size == 0 or entries[package.name] != digest(package):
                raise ValueError(f'Integridad incorrecta de {package.name}.')
            checksums.append(f'{entries[package.name]}  {package.name}\n')
    rar_name = f'ArcanaArena-v{version}-Windows-x64.rar'
    rar_path, rar_manifest = dist / rar_name, dist / 'SHA256SUMS-RAR.txt'
    has_rar = rar_path.exists() or rar_manifest.exists()
    if has_rar:
        if not rar_path.is_file() or not rar_manifest.is_file():
            raise ValueError('El RAR de Windows exige paquete y manifiesto de integridad.')
        with rar_path.open('rb') as stream:
            if stream.read(8) != b'Rar!\x1a\x07\x01\x00':
                raise ValueError('El paquete de Windows no es RAR 5.')
        expected = f'{digest(rar_path)}  {rar_name}'
        if rar_manifest.read_text(encoding='utf-8-sig').strip() != expected:
            raise ValueError('Integridad incorrecta del RAR de Windows.')
        checksums.append(expected + '\n')
    (dist / 'SHA256SUMS.txt').write_text(''.join(checksums), encoding='utf-8')
    tag = f'preview-v{version}-r{build}'
    metadata = dict(channel='preview', version=version, branch=branch, commit=commit, build=build, tag=tag)
    (dist / 'PREVIEW.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    packages = 'Windows ZIP y RAR' if has_rar else 'Windows'
    notes_file.write_text('Arcana-Preview-Branch: ' + branch + '\n\n'
                          + f'Versión de pruebas {version}, compilación {build}.\n'
                          + f'Commit: {commit}\n\n' + notes(root, version) + '\n'
                          + packages + ' y APK Android con firma permanente. Paquetes completos y deltas de esta rama con SHA-256 verificado.\n', encoding='utf-8')
    tag_file.write_text(tag + '\n', encoding='utf-8')
    return metadata


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('.'))
    parser.add_argument('--dist', type=Path, required=True)
    parser.add_argument('--branch', required=True)
    parser.add_argument('--commit', required=True)
    parser.add_argument('--build', type=int, required=True)
    parser.add_argument('--notes-file', type=Path, required=True)
    parser.add_argument('--tag-file', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.root, args.dist, args.branch, args.commit, args.build, args.notes_file, args.tag_file)))
