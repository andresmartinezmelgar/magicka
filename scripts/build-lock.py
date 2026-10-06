#!/usr/bin/env python3
"""Ejecuta una compilación con el mismo bloqueo de byte que PowerShell/.NET."""
import contextlib
import os
from pathlib import Path
import signal
import subprocess
import sys


@contextlib.contextmanager
def lease(repository):
    path = Path(repository).resolve() / '.tools' / 'arcana-build.lock'
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor = os.open(path, os.O_RDWR | os.O_CREAT | getattr(os, 'O_BINARY', 0), 0o600)
    with os.fdopen(descriptor, 'r+b', buffering=0) as stream:
        try:
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.lockf(stream, fcntl.LOCK_EX | fcntl.LOCK_NB, 1, 0)
        except OSError as error:
            raise BlockingIOError(f'Ya hay una compilación de Arcana Arena en esta carpeta. Bloqueo: {path}') from error
        # El cierre libera el bloqueo también tras una excepción o caída del proceso.
        yield


def main():
    if len(sys.argv) < 3:
        sys.exit('Uso: build-lock.py CARPETA COMANDO [ARGUMENTOS]')
    try:
        with lease(sys.argv[1]):
            child = subprocess.Popen(sys.argv[2:])

            def forward(number, _frame):
                if child.poll() is None:
                    if os.name == 'nt':
                        child.terminate()
                    else:
                        child.send_signal(number)

            signal.signal(signal.SIGTERM, forward)
            signal.signal(signal.SIGINT, forward)
            return child.wait()
    except BlockingIOError as error:
        print(str(error), file=sys.stderr)
        return 75


if __name__ == '__main__':
    sys.exit(main())
