#!/usr/bin/env python3
"""Synthetic byte study, not a game/phone benchmark. Uses the system Brotli library.

Run: python3 scripts/study-network-compression.py --output docs/benchmarks/network-2026-11-08.json
No build, game server, Firebase access or dependencies are required.
"""
import argparse
import base64
import ctypes
import ctypes.util
import json
import math
import platform
import random
import struct
import time
from pathlib import Path


def brotli():
    lib = ctypes.CDLL(ctypes.util.find_library("brotlienc") or "libbrotlienc.so.1")
    compress = lib.BrotliEncoderCompress
    compress.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_size_t, ctypes.c_void_p,
                         ctypes.POINTER(ctypes.c_size_t), ctypes.c_void_p]
    compress.restype = ctypes.c_int
    def encode(raw, quality):
        buffer = ctypes.create_string_buffer(len(raw) * 2 + 1024)
        length = ctypes.c_size_t(len(buffer))
        if not compress(quality, 22, 0, len(raw), raw, ctypes.byref(length), buffer):
            raise RuntimeError("Brotli compression failed")
        return buffer.raw[:length.value]
    decoder = ctypes.CDLL(ctypes.util.find_library("brotlidec") or "libbrotlidec.so.1").BrotliDecoderDecompress
    decoder.argtypes = [ctypes.c_size_t, ctypes.c_void_p, ctypes.POINTER(ctypes.c_size_t), ctypes.c_void_p]
    decoder.restype = ctypes.c_int
    def verify(raw, compressed):
        output = ctypes.create_string_buffer(len(raw))
        length = ctypes.c_size_t(len(raw))
        if decoder(len(compressed), compressed, ctypes.byref(length), output) != 1 or output.raw[:length.value] != raw:
            raise RuntimeError("Brotli round trip changed the packet")
    encode.verify = verify
    return encode


def frames(count):
    rng = random.Random(271)
    enemies = [(rng.uniform(0, 120), rng.uniform(0, 90), rng.uniform(-1, 1)) for _ in range(count)]
    for frame in range(120):
        binary = bytearray(struct.pack("<H", count))
        for i, (x, y, direction) in enumerate(enemies):
            # Same field widths as MinionDeltaCodec: id, mask, position, facing, HP and two timers.
            binary += struct.pack("<iHfffffff", 1000 + i, 1 | 2 | 4 | 16 | 32,
                                  x + frame * .035, y + math.sin(frame * .07 + i), direction,
                                  math.sqrt(1 - direction * direction), max(0, 500 - frame * (i % 3)),
                                  max(0, 4 - frame * .05), max(0, 2 - frame * .05))
        packet = {"type":"delta", "baseTick":(frame // 40) * 120, "baseId":1 + frame // 40,
                  "changes":{"snapshot":{"tick":frame * 3,
                    "players":{"$array":{"0":{"position":{"x":30 + frame * .05,"y":20},
                    "energy":100 - frame % 50, "damage":{"health":[frame * 1.1,0,0,0,frame * 3.7,0,0,0,0],
                    "shield":[0] * 9}}}},
                    "defense":{"elapsed":frame * .05,"nextBossIn":max(0, 90 - frame * .05)}}},
                  "minions":base64.b64encode(binary).decode()}
        yield json.dumps(packet, separators=(",", ":")).encode()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    encode = brotli()
    rows = []
    for count in (56, 89, 192):
        packets = list(frames(count))
        result = {"minions":count, "frames":len(packets), "uncompressedBytes":sum(map(len, packets))}
        for quality in (1, 3, 4):
            for packet in packets[:5]:
                encode(packet, quality)
            durations, size = [], 0
            for _ in range(3):
                started = time.perf_counter()
                compressed = [encode(packet, quality) for packet in packets]
                durations.append((time.perf_counter() - started) * 1000)
                size = sum(min(len(raw), len(zipped) + 1) for raw, zipped in zip(packets, compressed))
            for raw, compressed_packet in zip(packets, compressed):
                encode.verify(raw, compressed_packet)
            result[f"q{quality}Bytes"] = size
            result[f"q{quality}MsPerFrame"] = round(sorted(durations)[1] / len(packets), 4)
        result["q3SavingPercent"] = round(100 * (1 - result["q3Bytes"] / result["q1Bytes"]), 2)
        result["q4SavingPercent"] = round(100 * (1 - result["q4Bytes"] / result["q1Bytes"]), 2)
        rows.append(result)
    measures = ['damage','shieldDamage','damageReceived','shieldAbsorbed','overkill','immuneHits',
                'hits','kills','deaths','spawns','casts','healing','healingReceived','shieldGranted',
                'shieldRepaired','aliveSeconds','wetSeconds','burningSeconds','frozenSeconds',
                'friendlyDamage','selfDamage','revives','revived','collected','goldEarned',
                'goldSpent','levels','spawnHealth','samples','valueSum','distance','captures']
    statistics = json.dumps({"type":"statistics", "statistics":{"schemaVersion":1,"runId":"a" * 32,
        "revision":25740,"gameVersion":"0.29.52","day":"2026-11-08","mode":"defense","map":"bastion",
        "network":True,"status":"playing","seconds":429,"humans":1,"bots":0,"difficulty":5,
        "winnerTeam":-1,"scores":[0,0],"droppedBuckets":0,"continuation":False,
        "metrics":[{"group":"attack", "key":i % 32, "measure":measures[i // 32],
                    "value":i * 347.1263} for i in range(900)]}}, separators=(",", ":")).encode()
    encode.verify(statistics, encode(statistics, 3))
    report = {"roundTripVerifiedPackets":1081, "kind":"synthetic-compression-only", "seed":271, "platform":platform.platform(),
        "method":"Native Brotli, window 22; median of 3 passes; JSON delta with binary minions in base64. No real game trace, Godot, .NET or Raspberry measurement.",
        "delta":rows, "statistics":{"rawBytes":len(statistics), "q3Bytes":len(encode(statistics, 3)) + 1}}
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
