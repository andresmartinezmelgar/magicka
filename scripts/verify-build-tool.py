#!/usr/bin/env python3
"""Comprueba una descarga antes de extraerla o ejecutarla; no usa la red."""
import argparse
import hashlib
import json
from pathlib import Path


def verify(path, name=None, hashes=None):
    hashes = hashes or json.loads(Path(__file__).with_name("build-tool-hashes.json").read_text())
    expected = hashes.get(name or path.name)
    if not expected:
        raise ValueError("La herramienta no tiene un SHA-256 aprobado")
    algorithm = expected.get("algorithm", "sha256") if isinstance(expected, dict) else "sha256"
    digest = expected["digest"] if isinstance(expected, dict) else expected
    with path.open("rb") as content:
        actual = hashlib.file_digest(content, algorithm).hexdigest()
    if actual.lower() != digest.lower():
        raise ValueError("La herramienta no coincide con el SHA-256 aprobado")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", type=Path)
    parser.add_argument("--name")
    args = parser.parse_args()
    verify(args.path, args.name)
    print("BUILD_TOOL_HASH_OK: " + (args.name or args.path.name))
