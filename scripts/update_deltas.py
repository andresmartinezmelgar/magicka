"""Paquetes Windows por archivos y APK exacta por copia/literales. Solo biblioteca estándar."""
import gzip
import hashlib
import json
import mmap
import re
import struct
import zipfile
from pathlib import Path

MAX_BYTES = 2 * 1024 ** 3
PROTECTED = frozenset({"account-server.json", "google-desktop-oauth.json", "service-account.json",
    "server/account-server.json", "server/service-account.json", "server/appsettings.json",
    "server/appsettings.production.json"})
GEAR = tuple(int.from_bytes(hashlib.sha256(bytes([i])).digest()[:8], "little") for i in range(256))


def version(value):
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", value):
        raise ValueError("Versión inválida")
    return tuple(map(int, value.split(".")))


def digest_file(path):
    with open(path, "rb") as source:
        return hashlib.file_digest(source, "sha256").digest()


def safe_path(name):
    name = name.replace("\\", "/").rstrip("/")
    if not name or len(name) > 240 or name.startswith("/") or re.search(r'[<>:"|?*\x00-\x1f]', name):
        raise ValueError("Ruta inválida en el paquete")
    for part in name.split("/"):
        if not part or part in (".", "..") or part.endswith((".", " ")) or re.match(
                r"(?i)^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", part):
            raise ValueError("Ruta insegura en el paquete")
    return name


def zip_files(archive):
    entries = archive.infolist()
    if len(entries) > 8192 or sum(e.file_size for e in entries) > 4 * 1024 ** 3:
        raise ValueError("Paquete demasiado grande")
    names, files = set(), {}
    for entry in entries:
        name = safe_path(entry.filename)
        if name.lower() in names or (entry.external_attr >> 16) & 0xf000 == 0xa000 or entry.flag_bits & 1:
            raise ValueError("Rutas duplicadas, enlaces o archivos cifrados")
        names.add(name.lower())
        if not entry.is_dir():
            files[name] = entry
    return files


def zip_hash(archive, entry):
    with archive.open(entry) as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def windows_patch(base, target, base_version, target_version, output):
    if version(base_version) >= version(target_version):
        raise ValueError("Un parche debe avanzar de versión")
    output = Path(output)
    with zipfile.ZipFile(base) as old, zipfile.ZipFile(target) as new:
        old_files, new_files = zip_files(old), zip_files(new)
        raw_manifest = new.read("UPDATE-MANIFEST.json")
        if len(raw_manifest) > 8 * 1024 ** 2:
            raise ValueError("Manifiesto demasiado grande")
        manifest = json.loads(raw_manifest)
        if manifest["version"] != target_version or len(manifest["files"]) > 8192:
            raise ValueError("Manifiesto de otra versión")
        if new.read("VERSION.txt").decode("utf-8-sig").strip() != target_version:
            raise ValueError("VERSION.txt no coincide")
        old_index = {p.lower(): e for p, e in old_files.items()}
        owned, changed, reused = set(), [], 0
        for file in manifest["files"]:
            path = safe_path(file["path"])
            if path.lower() in owned or path not in new_files or path == "UPDATE-MANIFEST.json":
                raise ValueError("Manifiesto incompleto o duplicado")
            owned.add(path.lower())
            entry = new_files[path]
            if entry.file_size != file["length"] or zip_hash(new, entry) != file["sha256"]:
                raise ValueError("Un archivo no coincide con el manifiesto")
            previous = old_index.get(path.lower())
            if path.lower() in PROTECTED or previous is None or previous.file_size != entry.file_size or \
                    zip_hash(old, previous) != file["sha256"]:
                changed.append(path)
            else:
                reused += entry.file_size
        if set(p.lower() for p in new_files if p != "UPDATE-MANIFEST.json") != owned:
            raise ValueError("Hay archivos ajenos al manifiesto")
        removed = [{"path": path, "length": entry.file_size, "sha256": zip_hash(old, entry)}
            for path, entry in old_files.items() if path.lower() not in owned and
            path.lower() not in PROTECTED and path != "UPDATE-MANIFEST.json"]
        metadata = {"format": "arcana-files-1", "baseVersion": base_version,
            "targetVersion": target_version, "manifestSha256": hashlib.sha256(raw_manifest).hexdigest(),
            "removed": removed}
        with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as patch:
            patch.writestr("UPDATE-DELTA.json", json.dumps(metadata, ensure_ascii=False))
            patch.writestr("UPDATE-MANIFEST.json", raw_manifest)
            for path in changed:
                with new.open(new_files[path]) as source, patch.open(path, "w") as destination:
                    while data := source.read(65536):
                        destination.write(data)
    return {"bytes": output.stat().st_size, "fullBytes": Path(target).stat().st_size,
        "reusedBytes": reused, "changedFiles": len(changed), "removedFiles": len(removed)}


def chunks(data, offset, length):
    """Cortes por contenido: se realinean aunque un archivo haya insertado bytes."""
    begin, fingerprint = offset, 0
    for at in range(offset, offset + length):
        fingerprint = ((fingerprint << 1) + GEAR[data[at]]) & 0xffffffffffffffff
        size = at + 1 - begin
        if size >= 16384 and ((fingerprint & 65535) == 0 or size >= 262144):
            yield begin, size
            begin, fingerprint = at + 1, 0
    if begin < offset + length:
        yield begin, offset + length - begin


def body_range(data, entry):
    at = entry.header_offset
    if at < 0 or at + 30 > len(data) or data[at:at + 4] != b"PK\x03\x04":
        raise ValueError("Cabecera ZIP inválida")
    name_bytes, extra_bytes = struct.unpack_from("<HH", data, at + 26)
    start = at + 30 + name_bytes + extra_bytes
    if entry.compress_size < 0 or start + entry.compress_size > len(data):
        raise ValueError("Contenido ZIP fuera de límites")
    return start, entry.compress_size


def android_patch(base, target, base_version, target_version, output):
    if version(base_version) >= version(target_version):
        raise ValueError("Un parche debe avanzar de versión")
    base, target, output = Path(base), Path(target), Path(output)
    if not 0 < base.stat().st_size <= MAX_BYTES or not 0 < target.stat().st_size <= MAX_BYTES:
        raise ValueError("APK demasiado grande o vacía")
    operations = []
    def add(opcode, offset, length):
        if not length:
            return
        if operations and operations[-1][0] == opcode and operations[-1][1] + operations[-1][2] == offset:
            operations[-1] = (opcode, operations[-1][1], operations[-1][2] + length)
        else:
            operations.append((opcode, offset, length))
    with base.open("rb") as old_stream, target.open("rb") as new_stream, \
            mmap.mmap(old_stream.fileno(), 0, access=mmap.ACCESS_READ) as old, \
            mmap.mmap(new_stream.fileno(), 0, access=mmap.ACCESS_READ) as new, \
            zipfile.ZipFile(base) as old_zip, zipfile.ZipFile(target) as new_zip:
        old_entries, new_entries = zip_files(old_zip), zip_files(new_zip)
        position = 0
        for entry in sorted(new_entries.values(), key=lambda e: e.header_offset):
            start, length = body_range(new, entry)
            if start < position:
                raise ValueError("Entradas ZIP solapadas")
            add(1, position, start - position)
            previous = old_entries.get(entry.filename)
            old_start, old_length = body_range(old, previous) if previous else (0, 0)
            if previous and old_length == length and hashlib.sha256(old[old_start:old_start + length]).digest() == \
                    hashlib.sha256(new[start:start + length]).digest():
                add(0, old_start, length)
            elif previous and length >= 65536 and old_length >= 65536:
                index = {}
                for at, size in chunks(old, old_start, old_length):
                    index.setdefault((size, hashlib.blake2b(old[at:at + size], digest_size=16).digest()), at)
                for at, size in chunks(new, start, length):
                    match = index.get((size, hashlib.blake2b(new[at:at + size], digest_size=16).digest()))
                    # Confirmar bytes: una colisión del índice nunca produce una APK diferente.
                    if match is not None and old[match:match + size] == new[at:at + size]:
                        add(0, match, size)
                    else:
                        add(1, at, size)
            else:
                add(1, start, length)
            position = start + length
        add(1, position, len(new) - position)
        if not 0 < len(operations) <= 262144:
            raise ValueError("Demasiadas instrucciones")
        with output.open("wb") as raw, gzip.GzipFile(fileobj=raw, mode="wb", mtime=0, compresslevel=6) as patch:
            patch.write(b"ARCDLT01")
            for text in (base_version, target_version):
                encoded = text.encode("utf-8")
                patch.write(struct.pack("<H", len(encoded)) + encoded)
            patch.write(struct.pack("<qq", len(old), len(new)))
            patch.write(digest_file(base) + digest_file(target))
            patch.write(struct.pack("<i", len(operations)))
            for opcode, offset, length in operations:
                patch.write(bytes([opcode]))
                if opcode == 0:
                    patch.write(struct.pack("<q", offset))
                patch.write(struct.pack("<q", length))
                if opcode == 1:
                    for at in range(offset, offset + length, 65536):
                        patch.write(new[at:min(at + 65536, offset + length)])
    return {"bytes": output.stat().st_size, "fullBytes": target.stat().st_size,
        "reusedBytes": sum(length for opcode, _, length in operations if opcode == 0),
        "operations": len(operations)}


def apply_android(base, patch_path, output, base_version, target_version):
    """Verificador independiente de publicación; el cliente implementa el lector en C#."""
    with gzip.open(patch_path, "rb") as patch, open(base, "rb") as source, open(output, "wb") as target:
        def exact(count):
            raw = patch.read(count)
            if len(raw) != count:
                raise ValueError("Parche truncado")
            return raw
        if exact(8) != b"ARCDLT01":
            raise ValueError("Formato inválido")
        versions = [exact(struct.unpack("<H", exact(2))[0]).decode() for _ in range(2)]
        base_bytes, target_bytes = struct.unpack("<qq", exact(16))
        base_hash, target_hash = exact(32), exact(32)
        if versions != [base_version, target_version] or base_bytes != Path(base).stat().st_size or \
                base_hash != digest_file(base) or not 0 < target_bytes <= MAX_BYTES:
            raise ValueError("Base incorrecta")
        count = struct.unpack("<i", exact(4))[0]
        if not 0 < count <= 262144:
            raise ValueError("Instrucciones inválidas")
        written = 0
        for _ in range(count):
            opcode = exact(1)[0]
            offset = struct.unpack("<q", exact(8))[0] if opcode == 0 else 0
            length = struct.unpack("<q", exact(8))[0]
            if opcode > 1 or length <= 0 or length > target_bytes - written or \
                    opcode == 0 and (offset < 0 or offset + length > base_bytes):
                raise ValueError("Instrucción fuera de límites")
            if opcode == 0:
                source.seek(offset)
            remaining = length
            while remaining:
                size = min(remaining, 65536)
                raw = source.read(size) if opcode == 0 else exact(size)
                if len(raw) != size:
                    raise ValueError("Base truncada")
                target.write(raw)
                remaining -= size
                written += size
        if patch.read(1) or written != target_bytes:
            raise ValueError("Resultado incompleto")
    if digest_file(output) != target_hash:
        raise ValueError("El resultado no es idéntico a la APK firmada")
