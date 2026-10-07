#!/usr/bin/env python3
"""Deriva los metadatos de exportacion de VERSION, sin dependencias externas."""
from pathlib import Path
import re
import sys


def parse_version(text):
    name = text.strip()
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', name):
        raise ValueError('VERSION debe contener tres enteros, como 0.12.19.')
    major, minor, patch = map(int, name.split('.'))
    if major > 2100 or minor > 999 or patch > 999:
        raise ValueError('VERSION excede el rango del codigo Android.')
    code = major * 1000000 + minor * 1000 + patch
    if not 1 <= code <= 2100000000:
        raise ValueError('El codigo Android debe estar entre 1 y 2100000000.')
    return {'name': name, 'windows': name + '.0', 'android_code': code}


def update_presets(text, version):
    values = {
        'application/file_version': '"' + version['windows'] + '"',
        'application/product_version': '"' + version['windows'] + '"',
        'version/name': '"' + version['name'] + '"',
        'version/code': str(version['android_code']),
    }
    for key, value in values.items():
        pattern = r'^' + re.escape(key) + r'=[^\r\n]*'
        if len(re.findall(pattern, text, re.M)) != 1:
            raise ValueError(f'El preset debe contener una sola entrada {key}.')
        text = re.sub(pattern, key + '=' + value, text, flags=re.M)
    pattern = r'^export_path="[^"\r\n]*\.apk"'
    if len(re.findall(pattern, text, re.M)) != 1:
        raise ValueError('Falta una ruta de exportacion Android unica.')
    return re.sub(pattern, 'export_path="../../artifacts/android/ArcanaArena-v' +
                  version['name'] + '-Android-arm64.apk"', text, flags=re.M)


def main():
    if len(sys.argv) != 3 or sys.argv[1] not in ('info', 'sync', 'check'):
        sys.exit('Uso: version-support.py info|sync|check CARPETA')
    root = Path(sys.argv[2])
    version = parse_version((root / 'VERSION').read_text(encoding='utf-8-sig'))
    if sys.argv[1] == 'info':
        print(version['name'], version['android_code'])
        return
    path = root / 'src/Arcana.Client/export_presets.cfg'
    with path.open(encoding='utf-8-sig', newline='') as stream:
        original = stream.read()
    updated = update_presets(original, version)
    if sys.argv[1] == 'check':
        if original != updated:
            sys.exit('Los presets de exportacion no coinciden con VERSION.')
        print('EXPORT_VERSIONS_OK:', version['name'], version['android_code'])
    else:
        with path.open('w', encoding='utf-8', newline='') as stream:
            stream.write(updated)


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError) as error:
        sys.exit(str(error))
