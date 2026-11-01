#!/usr/bin/env python3
"""Compose Arcana Arena's original, sample-free score (NumPy and FFmpeg).

All stems share 16 bars of 4/4 at 96 BPM, D minor, and exactly 960000
stereo frames at 24 kHz. Notes and reverberation wrap around the loop.
Run from any directory; the game only needs the resulting Ogg files.
"""
from pathlib import Path
import hashlib
import json
import subprocess
import tempfile
import wave

import numpy as np

RATE = 24000
BPM = 96
BEAT = 60 / BPM
BARS = 16
SECONDS = BARS * 4 * BEAT
FRAMES = round(SECONDS * RATE)
DESTINATION = Path(__file__).resolve().parents[1] / "src/Arcana.Client/Assets/Audio"


def frequency(midi):
    return 440 * 2 ** ((midi - 69) / 12)


def add(stem, signal, beat, pan=0):
    """Equal-power stereo placement; tails continue through the loop seam."""
    indices = (np.arange(len(signal)) + round(beat * BEAT * RATE)) % FRAMES
    stem[indices, 0] += signal * np.sqrt((1 - pan) / 2)
    stem[indices, 1] += signal * np.sqrt((1 + pan) / 2)


def note(midi, seconds, instrument, strength):
    t = np.arange(round(seconds * RATE)) / RATE
    f = frequency(midi)
    fade = np.minimum(1, (seconds - t) / 0.12)
    if instrument == "strings":
        attack = np.minimum(1, t / 0.65)
        signal = sum(np.sin(2 * np.pi * f * ratio * t + 0.15 * np.sin(2 * np.pi * 0.3 * t))
                     * weight for ratio, weight in ((0.998, .36), (1.002, .36), (2, .15), (3, .06)))
        envelope = attack * fade * (0.88 + 0.12 * np.sin(np.pi * t / seconds))
    elif instrument == "harp":
        signal = sum(np.sin(2 * np.pi * f * harmonic * t) * np.exp(-t * decay) * weight
                     for harmonic, decay, weight in ((1, 2, .7), (2, 4, .2), (3, 7, .1)))
        envelope = np.minimum(1, t / .009) * fade
    elif instrument == "bell":
        signal = np.sin(2 * np.pi * f * t + 1.8 * np.exp(-t * 4) * np.sin(2 * np.pi * f * 2 * t))
        envelope = np.minimum(1, t / .006) * np.exp(-t * 2.2) * fade
    else:  # rounded, quiet bass ostinato
        signal = np.sin(2 * np.pi * f * t) + .2 * np.sin(2 * np.pi * f * 2 * t)
        envelope = np.minimum(1, t / .018) * np.exp(-t * 3.1) * fade
    return signal * envelope * strength


def reverb(stem, amount):
    dry = stem.copy()
    for seconds, gain in ((.173, .28), (.311, .2), (.487, .12), (.719, .07)):
        stem += np.roll(dry[:, ::-1], round(seconds * RATE), axis=0) * gain * amount
    return stem


def compose():
    stems = {name: np.zeros((FRAMES, 2), dtype=np.float64)
             for name in ("arcane_atmosphere", "arcane_battle", "arcane_danger")}
    air, battle, danger = stems.values()
    rng = np.random.default_rng(29034)
    # Dm(add9), Bb(add9), F(add9), C(sus2), twice with melodic variation.
    chords = ((50, 57, 62, 65, 69), (46, 53, 58, 62, 65),
              (41, 48, 57, 60, 65), (48, 55, 62, 64, 67))
    melody = ((74, 77, 81, 77), (77, 74, 72, 70),
              (69, 72, 77, 81), (79, 76, 74, 72))
    for bar in range(BARS):
        chord = chords[(bar // 2) % 4]
        beat = bar * 4
        if bar % 2 == 0:
            for index, midi in enumerate(chord):
                add(air, note(midi, 8 * BEAT + .7, "strings", .09 if index else .13),
                    beat, (index - 2) * .27)
        for step in range(8):
            midi = chord[(0, 2, 3, 1, 4, 2, 3, 1)[step]] + 12
            add(air, note(midi, 1.9, "harp", .075 if step % 2 == 0 else .05),
                beat + step * .5, (-1 if step % 2 else 1) * .38)
            add(battle, note(chord[0] - 12, .65, "bass", .24 if step % 2 == 0 else .12),
                beat + step * .5)
            # Thin, high pulsing strings appear only in dangerous situations.
            add(danger, note(chord[2] + (12 if step % 2 else 0), .34, "strings", .11),
                beat + step * .5, (-1 if step % 2 else 1) * .22)
        if bar % 2:
            phrase = melody[(bar // 2) % 4]
            for step, midi in enumerate(phrase):
                add(air, note(midi + (12 if bar >= 8 and step == 2 else 0), 2.4, "bell", .065),
                    beat + step, (step - 1.5) * .25)
        for step, strength in ((0, .32), (1.5, .13), (2, .24), (3.5, .11)):
            t = np.arange(round(.65 * RATE)) / RATE
            phase = 2 * np.pi * (46 * t + 3.5 * (1 - np.exp(-t * 28)))
            drum = np.sin(phase) * np.exp(-t * 9) * np.minimum(1, t / .003)
            add(battle, drum * strength, beat + step, -.08)
        for step in (1, 3):
            t = np.arange(round(.35 * RATE)) / RATE
            noise = rng.normal(0, 1, len(t))
            noise = np.convolve(noise, np.ones(7) / 7, mode="same")
            drum = (noise * .42 + np.sin(2 * np.pi * 155 * t) * .3) * np.exp(-t * 16)
            add(battle, drum * .22 * np.minimum(1, t / .003), beat + step, .18)
        # Gentle metallic rhythmic accent; not a shrill warning alarm.
        add(danger, note(chord[3] + 12, 2.5, "bell", .12), beat, .3)
    reverb(air, 1)
    reverb(battle, .3)
    reverb(danger, .65)
    # One shared gain preserves the orchestration and leaves mix headroom.
    peak = max(float(np.max(np.abs(stem))) for stem in stems.values())
    return {name: stem * (.54 / peak) for name, stem in stems.items()}


def main():
    DESTINATION.mkdir(parents=True, exist_ok=True)
    tracks = []
    with tempfile.TemporaryDirectory(prefix="arcana-music-") as temporary:
        for name, stem in compose().items():
            source = Path(temporary) / f"{name}.wav"
            with wave.open(str(source), "wb") as output:
                output.setnchannels(2)
                output.setsampwidth(2)
                output.setframerate(RATE)
                output.writeframes(np.rint(stem * 32767).astype("<i2").tobytes())
            target = DESTINATION / f"{name}.ogg"
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(source),
                            "-c:a", "libvorbis", "-q:a", "4", "-map_metadata", "-1",
                            "-metadata", "title=" + name, "-metadata", "artist=Arcana Arena",
                            "-metadata", "license=CC0-1.0", str(target)], check=True)
            tracks.append({"file": target.name, "frames": FRAMES,
                           "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
            print(f"{target.name}: {SECONDS:g}s, {target.stat().st_size} bytes")
    (DESTINATION / "soundtrack.json").write_text(json.dumps({
        "title": "Resonancia del santuario", "license": "CC0-1.0", "bpm": BPM,
        "bars": BARS, "beats_per_bar": 4, "key": "D minor", "sample_rate": RATE,
        "channels": 2, "tracks": tracks,
    }, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
