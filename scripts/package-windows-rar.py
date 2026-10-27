"""Create a real RAR 5 package and verify every extracted byte against the Windows ZIP."""
import argparse
import hashlib
from pathlib import Path, PurePosixPath
import stat
import subprocess
import tempfile
import zipfile


def hashes(directory):
    result = {}
    for path in sorted(directory.rglob('*')):
        if path.is_file():
            with path.open('rb') as stream:
                result[path.relative_to(directory).as_posix()] = hashlib.file_digest(stream, 'sha256').hexdigest()
    return result


def package(source, output, rar):
    source, output = source.resolve(), output.resolve()
    if source == output or output.suffix.lower() != '.rar':
        raise ValueError('The output must be a separate .rar file.')
    with tempfile.TemporaryDirectory(prefix='arcana-windows-rar-') as temporary:
        temporary = Path(temporary)
        unpacked, verified = temporary / 'package', temporary / 'verified'
        unpacked.mkdir(); verified.mkdir()
        with zipfile.ZipFile(source) as archive:
            for entry in archive.infolist():
                path = PurePosixPath(entry.filename)
                if path.is_absolute() or '..' in path.parts or '\\' in entry.filename or ':' in entry.filename or stat.S_ISLNK(entry.external_attr >> 16):
                    raise ValueError('Unsafe Windows ZIP entry: ' + entry.filename)
            if archive.testzip() is not None:
                raise ValueError('The Windows ZIP is corrupt.')
            archive.extractall(unpacked)
        expected = hashes(unpacked)
        if 'ArcanaArena.exe' not in expected or 'VERSION.txt' not in expected:
            raise ValueError('The ZIP does not contain the complete Windows game.')
        candidate = temporary / 'ArcanaArena.rar'
        subprocess.run([rar, 'a', '-ma5', '-m4', '-md32m', '-mt2', '-r', '-idq', str(candidate), '.'], cwd=unpacked, check=True)
        with candidate.open('rb') as stream:
            if stream.read(8) != b'Rar!\x1a\x07\x01\x00':
                raise ValueError('The archiver did not produce a RAR 5 archive.')
        subprocess.run([rar, 't', '-idq', str(candidate)], check=True)
        subprocess.run([rar, 'x', '-idq', str(candidate), str(verified) + '/'], check=True)
        if hashes(verified) != expected:
            raise ValueError('The RAR changes or omits files from the Windows package.')
        output.parent.mkdir(parents=True, exist_ok=True)
        # Move only after integrity and extraction checks succeed, across file systems if needed.
        import shutil
        shutil.copyfile(candidate, output)
        print(f'WINDOWS_RAR_OK: {len(expected)} files, RAR 5 signature, CRC and SHA-256 verified; {output.stat().st_size} bytes.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--rar', default='rar')
    args = parser.parse_args()
    package(args.source, args.output, args.rar)
