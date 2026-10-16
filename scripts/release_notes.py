"""Resumen público breve obtenido únicamente de la versión exacta del changelog."""
import argparse
import re
from pathlib import Path

START = '<!-- Arcana-Changes -->'
END = '<!-- /Arcana-Changes -->'


def notes(root, version):
    path = root / 'CHANGELOG.md'
    text = path.read_text(encoding='utf-8-sig') if path.is_file() else ''
    section = re.search(r'^## ' + re.escape(version) + r'(?:[ \t]+[^\n]*)?\r?\n(.*?)(?=^## |\Z)', text, re.M | re.S)
    entries = [] if not section else re.findall(r'^- (.+)$', section[1], re.M)[:5]
    entries = [entry if len(entry) <= 200 else entry[:199] + '…' for entry in entries]
    summary = '\n'.join('- ' + entry for entry in entries)
    return f'## Novedades de {version}\n\n{START}\n{summary}\n{END}\n'


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('.'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    version = (args.root / 'VERSION').read_text(encoding='utf-8-sig').strip()
    args.output.write_text(notes(args.root, version), encoding='utf-8')
