#!/usr/bin/env python3
"""Prepara una publicación de pruebas con los mismos binarios firmados de CI."""
import argparse
import hashlib
import json
import re
from pathlib import Path


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
        entries = [line.split(maxsplit=1) for line in manifest.read_text().splitlines()]
        expected = [parts[0] for parts in entries if len(parts) == 2 and parts[1].lstrip('*') == name]
        actual = digest(path)
        if len(expected) != 1 or expected[0].lower() != actual:
            raise ValueError(f'Integridad incorrecta de {name}.')
        checksums.append(f'{actual}  {name}\n')
    (dist / 'SHA256SUMS.txt').write_text(''.join(checksums), encoding='utf-8')
    tag = f'preview-v{version}-r{build}'
    metadata = dict(channel='preview', version=version, branch=branch, commit=commit, build=build, tag=tag)
    (dist / 'PREVIEW.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    notes_file.write_text('Arcana-Preview-Branch: ' + branch + '\n\n'
                          + f'Versión de pruebas {version}, compilación {build}.\n'
                          + f'Commit: {commit}\n\n'
                          + 'Windows y APK Android con firma permanente. Descarga directa y SHA-256 verificado.\n', encoding='utf-8')
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
