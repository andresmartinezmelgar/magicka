#!/usr/bin/env python3
"""Comprueba exclusión entre procesos y liberación tras éxito, fallo y terminación."""
from pathlib import Path
import subprocess
import sys
import tempfile
import time

launcher = Path(__file__).with_name('build-lock.py')
with tempfile.TemporaryDirectory(prefix='arcana lock ') as temporary:
    root = Path(temporary) / 'proyecto [con espacios]'
    root.mkdir()
    ready, release, unwanted = (root / name for name in ('ready', 'release', 'unwanted'))
    holder = root / 'holder.py'
    holder.write_text('''from pathlib import Path
import sys, time
Path(sys.argv[1]).write_text('ready')
while not Path(sys.argv[2]).exists(): time.sleep(0.02)
''')

    def command(*arguments):
        return [sys.executable, str(launcher), str(root), sys.executable, *arguments]

    def check_available():
        result = subprocess.run(command('-c', 'pass'), capture_output=True)
        assert result.returncode == 0, result.stderr.decode()

    for stop in ('normal', 'terminate'):
        ready.unlink(missing_ok=True)
        release.unlink(missing_ok=True)
        process = subprocess.Popen(command(str(holder), str(ready), str(release)))
        try:
            deadline = time.monotonic() + 5
            while not ready.exists() and time.monotonic() < deadline:
                assert process.poll() is None, 'El primer proceso terminó antes de adquirir el bloqueo.'
                time.sleep(0.02)
            assert ready.exists(), 'No se adquirió el bloqueo.'
            blocked = subprocess.run(command('-c', 'from pathlib import Path; import sys; Path(sys.argv[1]).touch()', str(unwanted)), capture_output=True)
            assert blocked.returncode == 75 and not unwanted.exists(), 'La segunda compilación alcanzó la fase de escritura.'
            # Una carpeta distinta puede compilar en paralelo.
            other = subprocess.run([sys.executable, str(launcher), str(root / 'otro'), sys.executable, '-c', 'pass'])
            assert other.returncode == 0
            if stop == 'normal': release.touch()
            else: process.terminate()
            process.wait(timeout=5)
        finally:
            release.touch()
            if process.poll() is None: process.terminate(); process.wait(timeout=5)
        check_available()
    failed = subprocess.run(command('-c', 'import sys; sys.exit(7)'))
    assert failed.returncode == 7, 'No se conservó el error de compilación.'
    check_available()
print('BUILD_LOCK_OK: procesos excluidos, carpetas independientes y liberación tras éxito, fallo y terminación.')
