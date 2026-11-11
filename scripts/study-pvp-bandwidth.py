#!/usr/bin/env python3
"""Synthetic PvP compression study; does not build or execute Arcana Arena.

Run: python3 scripts/study-pvp-bandwidth.py --output docs/benchmarks/pvp-network-2026-11-09.json

The fixtures follow Protocol.cs / SnapshotDelta.cs, but are NOT a game recording
or a substitute for the C# tests. Python JSON numbers need not have the same text
as System.Text.Json floats. Timings include Python/ctypes/native Brotli overhead,
not .NET, Godot, networking, Raspberry Pi or Android execution.
"""
import argparse
import copy
import hashlib
import importlib.util
import json
import math
import platform
import random
import statistics
import sys
import time
from pathlib import Path


def native_brotli():
    # This standalone study should not leave a cache file in the repository.
    sys.dont_write_bytecode = True
    helper = Path(__file__).with_name("study-network-compression.py")
    spec = importlib.util.spec_from_file_location("network_compression_study", helper)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.brotli()


def wire(value):
    return json.dumps(value, separators=(",", ":"), ensure_ascii=True).encode()


def entity_keys(values):
    keys = []
    for value in values:
        if not isinstance(value, dict):
            return None
        identity = value.get("id", value.get("playerId", value.get("item", {}).get("id")))
        if identity is None:
            return None
        key = wire(identity).decode()
        if key in keys:
            return None
        keys.append(key)
    return keys


UNCHANGED = object()


def diff(previous, current):
    """Mirror the existing JSON delta representations for these fixtures."""
    if previous == current:
        return UNCHANGED
    if isinstance(previous, dict) and isinstance(current, dict):
        changes = {key: {"$null": True} for key in previous if key not in current}
        for key, value in current.items():
            change = diff(previous.get(key), value)
            if change is not UNCHANGED:
                changes[key] = change
        return changes
    if isinstance(previous, list) and isinstance(current, list):
        old_keys, new_keys = entity_keys(previous), entity_keys(current)
        if previous and current and old_keys is not None and new_keys is not None and old_keys != new_keys:
            old = dict(zip(old_keys, previous))
            updates = {}
            for key, value in zip(new_keys, current):
                change = diff(old.get(key), value)
                if change is not UNCHANGED:
                    updates[key] = change
            return {"$entities": {"order": new_keys, "updates": updates}}
        if len(previous) == len(current):
            changes = {}
            for index, (old, new) in enumerate(zip(previous, current)):
                change = diff(old, new)
                if change is not UNCHANGED:
                    changes[str(index)] = change
            return {"$array": changes}
    return {"$null": True} if current is None else copy.deepcopy(current)


def merge(previous, changes):
    if changes == {"$null": True}:
        return None
    if isinstance(previous, list) and isinstance(changes, dict):
        if "$entities" in changes:
            old = dict(zip(entity_keys(previous), previous))
            patch = changes["$entities"]
            return [merge(copy.deepcopy(old.get(key)), patch["updates"][key])
                    if key in patch["updates"] else copy.deepcopy(old[key]) for key in patch["order"]]
        if "$array" in changes:
            for index, value in changes["$array"].items():
                previous[int(index)] = merge(previous[int(index)], value)
            return previous
    if isinstance(previous, dict) and isinstance(changes, dict):
        for key, value in changes.items():
            previous[key] = merge(previous.get(key), value)
        return previous
    return copy.deepcopy(changes)


def vec(x, y):
    return {"x": round(x, 6), "y": round(y, 6)}


def wizard(index, frame, activity):
    phase = index * 0.7 + (frame * 0.012 if activity != "idle" else 0)
    player = {
        "id": index + 1, "name": f"Mago {index + 1}", "team": index % 2, "bot": False,
        "position": vec(10 * math.cos(phase), 9 * math.sin(phase)),
        "facing": vec(math.cos(phase), math.sin(phase)), "health": 1000, "maxHealth": 1000,
        "shield": 0, "ward": [], "selected": [], "alive": True, "respawn": 0,
        "wet": 0, "burning": 0, "chill": 0, "frozen": 0, "haste": 0,
        "energy": 100, "cooldowns": [0, 0, 0, 0], "kills": 0, "deaths": 0,
        "charge": 0, "casting": False, "shieldMax": 0, "shieldSeconds": 0,
        "wizardType": "balanced", "regenerationRate": 0, "buffs": [],
        "castingElements": [], "shieldCharges": 1, "invulnerableSeconds": 0,
        "equipment": None, "reanimationCost": 60,
    }
    if activity == "combat":
        player.update(health=round(1000 - (frame * (index + 1) * 0.31) % 600, 4),
                      energy=round(100 - (frame * 0.17 + index) % 60, 4),
                      cooldowns=[round(max(0, period - (frame % 80) * 0.05), 4) for period in (4, 7, 15, 30)],
                      charge=round((frame % 30) / 30, 6), casting=True,
                      castingElements=["fire", "arcane"],
                      damage={"health": [round(frame * (index + 1) * 1.23, 4) if n == 7 else 0 for n in range(9)],
                              "shield": [round(frame * 0.11, 4) if n == 5 else 0 for n in range(9)]})
    return player


def frame_message(count, frame, activity):
    players = [wizard(index, frame, activity) for index in range(count)]
    snapshot = {"tick": frame * 3, "time": round(frame * 0.05, 6),
                "mode": "duel" if count == 2 else "arena", "matchEnded": False,
                "winner": -1, "restartIn": 0, "scores": [0, 0], "players": players,
                "projectiles": [], "walls": [], "fields": [], "beams": [], "points": [], "events": [],
                "scoreLimit": 15, "startingTickets": 75, "map": "classic",
                "holdingTeam": -1, "holdSeconds": 0, "pickups": [], "waitingForPlayers": False,
                "lobbyHostId": 0, "lobbyReadyPlayerIds": None, "bestOf": 1, "seriesWins": None,
                "rewardStatus": "", "awaitingRematch": False, "defense": None,
                "statisticsRunId": "a" * 32}
    if activity == "combat":
        snapshot["projectiles"] = [
            {"id": 100 + index, "owner": index + 1,
             "position": vec(players[index]["position"]["x"] + frame % 12 * 0.2, players[index]["position"]["y"]),
             "direction": players[index]["facing"], "elements": ["earth", "fire"], "radius": 0.4}
            for index in range(count)]
        # A bounded synthetic event history (1.5 s) exercises entity additions/removals.
        snapshot["events"] = [{"id": event + 1, "kind": "hit", "position": vec(event % 8, event % 5),
                                "elements": ["fire"], "text": "12", "radius": 0, "actorId": event % count + 1}
                               for event in range(max(0, frame - 29), frame + 1, 3)]
    return {"type": "snapshot", "playerId": 0, "error": "", "server": None, "room": None,
            "snapshot": snapshot, "requiresReady": False, "preparingPlayers": 0,
            "readyPlayerIds": list(range(1, count + 1))}


def packets(count, activity, frame_count):
    baseline = frame_message(count, 0, activity)
    base_id = 1
    result = []
    for frame in range(1, frame_count + 1):
        current = frame_message(count, frame, activity)
        changes = diff(baseline, current)
        if merge(copy.deepcopy(baseline), changes) != current:
            raise AssertionError("The synthetic delta did not reconstruct its fixture")
        delta = {"type": "delta", "baseTick": baseline["snapshot"]["tick"],
                 "baseId": base_id, "changes": changes}
        result.append(("delta", wire(delta)))
        if frame % 40 == 0:
            keyframe = dict(current, type="keyframe", baselineId=base_id + 1)
            result.append(("keyframe", wire(keyframe)))
            # Immediate acknowledgement before the next frame, no retries/loss.
            baseline, base_id = current, base_id + 1
    return result


def policy(raw, threshold, encode):
    if len(raw) < threshold:
        return raw
    compressed = encode(raw, 3)
    # Protocol.CompressSnapshotPayload: written + 17 < raw.Length.
    return b"\xa1" + compressed if len(compressed) + 17 < len(raw) else raw


def measure(samples, threshold, encode):
    for _, raw in samples[:8]:
        policy(raw, threshold, encode)
    durations = []
    for _ in range(5):
        started = time.perf_counter_ns()
        output = [policy(raw, threshold, encode) for _, raw in samples]
        durations.append((time.perf_counter_ns() - started) / 1_000_000)
    compressed_count = 0
    for (_, raw), payload in zip(samples, output):
        if payload is raw:
            if payload != raw:
                raise AssertionError("Raw passthrough differs")
        else:
            encode.verify(raw, payload[1:])
            compressed_count += 1
        if len(payload) > len(raw):
            raise AssertionError("Compression expanded a packet")
    return output, statistics.median(durations), compressed_count


def array_examples(encode):
    examples = [
        ("four_active_cooldowns", [4, 7, 15, 30], [3.95, 6.95, 14.95, 29.95]),
        ("one_damage_bucket", [0] * 9, [0, 0, 0, 0, 0, 0, 0, 123.45, 0]),
        ("two_selected_elements", ["fire", "fire"], ["arcane", "earth"]),
    ]
    result = []
    for name, before, after in examples:
        patch = wire(diff(before, after))
        literal = wire(after)
        for raw in (patch, literal):
            encode.verify(raw, encode(raw, 3))
        result.append({"name": name, "patchRawBytes": len(patch), "literalRawBytes": len(literal),
                       "patchBrotliBytes": len(encode(patch, 3)) + 1,
                       "literalBrotliBytes": len(encode(literal, 3)) + 1})
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    encode = native_brotli()
    frame_count = 240
    seconds = frame_count / 20
    rows = []
    verified = 0
    digest = hashlib.sha256()
    for count in (2, 4, 8, 16):
        for activity in ("idle", "movement", "combat"):
            samples = packets(count, activity, frame_count)
            for _, raw in samples:
                digest.update(len(raw).to_bytes(4, "little"))
                digest.update(raw)
            old, old_ms, old_compressed = measure(samples, 1024, encode)
            new, new_ms, new_compressed = measure(samples, 256, encode)
            verified += len(samples) * 2
            old_bytes, new_bytes = sum(map(len, old)), sum(map(len, new))
            deltas = [raw for kind, raw in samples if kind == "delta"]
            rows.append({"players": count, "activity": activity, "seconds": seconds,
                         "deltaPackets": len(deltas), "keyframePackets": len(samples) - len(deltas),
                         "minDeltaRawBytes": min(map(len, deltas)), "maxDeltaRawBytes": max(map(len, deltas)),
                         "deltasBetween256And1023": sum(256 <= len(raw) < 1024 for raw in deltas),
                         "rawBytes": sum(len(raw) for _, raw in samples),
                         "beforeBytes": old_bytes, "afterBytes": new_bytes,
                         "beforePayloadBytesPerMinute": round(old_bytes * 60 / seconds),
                         "afterPayloadBytesPerMinute": round(new_bytes * 60 / seconds),
                         "savingPercent": round(100 * (1 - new_bytes / old_bytes), 2),
                         "beforeCompressedPackets": old_compressed, "afterCompressedPackets": new_compressed,
                         "beforeCompressionMsPerFrame": round(old_ms / frame_count, 6),
                         "afterCompressionMsPerFrame": round(new_ms / frame_count, 6),
                         "incrementalCompressionMsPerFrame": round((new_ms - old_ms) / frame_count, 6)})
    rng = random.Random(271)
    boundaries = [bytes(rng.randrange(256) for _ in range(n)) for n in (0, 1, 255, 256, 1023, 1024, 8192)]
    boundaries += [b"a" * n for n in (255, 256, 1023, 1024)]
    for raw in boundaries:
        for threshold in (256, 1024):
            payload = policy(raw, threshold, encode)
            if payload is not raw:
                encode.verify(raw, payload[1:])
            assert len(payload) <= len(raw)
            if len(raw) < threshold:
                assert payload is raw
            verified += 1
    arrays = array_examples(encode)
    report = {"schema": 1, "kind": "synthetic-pvp-compression-only", "seed": 271,
              "platform": platform.platform(), "python": platform.python_version(),
              "fixtureSha256": digest.hexdigest(), "roundTripOrPassthroughChecks": verified + 6,
              "method": {"snapshotHz": 20, "keyframeEveryFrames": 40, "quality": 3, "window": 22,
                         "oldThresholdBytes": 1024, "newThresholdBytes": 256,
                         "acceptCompressedWhen": "compressedBytes + 17 < rawBytes",
                         "timing": "Median of five passes, eight warmup messages; Python/ctypes and native Brotli, including keyframes, per simulation frame.",
                         "acknowledgement": "Immediate before the next delta, no loss or retry.",
                         "fixtures": "Protocol.cs field names, deterministic idle/movement/combat states and SnapshotDelta JSON representations; not C# serializer output.",
                         "excluded": ["welcome", "statistics", "control messages", "uplink", "WebSocket headers", "TCP/TLS/IP", "HTTP/Firebase", "retransmissions"],
                         "limitations": "Synthetic 12-second payload samples extrapolated to one minute. Not real game traffic, .NET CPU, Android FPS, or Raspberry Pi measurements. Python numeric spelling can differ from System.Text.Json. Timing differences near zero are noise."},
              "scenarios": rows,
              "scalarArraysExploration": {"adopted": False,
                  "note": "Isolated fields only, forced Brotli for comparison; not actual separate packets or a prediction after whole-frame compression.",
                  "examples": arrays}}
    text = json.dumps(report, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text)
    print(text, end="")


if __name__ == "__main__":
    main()
