#!/usr/bin/env python3
"""Compose Arcana Arena's original, sample-free score (NumPy and FFmpeg).

All stems share 24 bars of 4/4 at 144 BPM, D minor, and exactly 960000
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
BPM = 144
BEAT = 60 / BPM
BARS = 24
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
    phase = 2 * np.pi * frequency(midi) * t
    release = np.minimum(1, (seconds - t) / min(.1, seconds * .45))
    if instrument == "strings":
        bow = .16 * np.sin(2 * np.pi * 5.1 * t)
        signal = (.55 * np.sin(phase * .997 + bow) + .34 * np.sin(phase * 1.003 - bow)
                  + .23 * np.sin(2 * phase) + .14 * np.sin(3 * phase) + .08 * np.sin(4 * phase))
        envelope = np.minimum(1, t / .012) * np.exp(-t * 2.5)
    elif instrument == "brass":
        brightness = 1 - .65 * np.exp(-t * 30)
        signal = .73 * np.sin(phase) + brightness * (.32 * np.sin(2 * phase)
                 + .2 * np.sin(3 * phase) + .09 * np.sin(4 * phase))
        envelope = np.minimum(1, t / .028) * np.exp(-t * 1.1)
    else:
        signal = .76 * np.sin(phase) + .33 * np.sin(2 * phase) + .18 * np.sin(3 * phase)
        envelope = np.minimum(1, t / .014) * np.exp(-t * 2)
    return signal * envelope * release * strength


def percussion():
    t = np.arange(round(.45 * RATE)) / RATE
    noise = np.random.default_rng(29034).normal(0, 1, len(t))
    low_noise = np.convolve(noise, np.ones(9) / 9, mode="same")
    high_noise = noise - low_noise
    edge = np.minimum(1, t / .003) * np.minimum(1, (.45 - t) / .025)
    kick = np.sin(2 * np.pi * (49 * t + 3.1 * (1 - np.exp(-t * 34)))) * np.exp(-t * 12)
    snare = (.42 * high_noise + .32 * low_noise + .32 * np.sin(2 * np.pi * 180 * t)) * np.exp(-t * 18)
    tom = np.sin(2 * np.pi * (83 * t + 1.8 * (1 - np.exp(-t * 23)))) * np.exp(-t * 13)
    hat = high_noise * np.exp(-t * 53)
    return kick * edge, snare * edge, tom * edge, hat * edge


def reverb(stem, amount):
    dry = stem.copy()
    for seconds, gain in ((.173, .28), (.311, .2), (.487, .12), (.719, .07)):
        stem += np.roll(dry[:, ::-1], round(seconds * RATE), axis=0) * gain * amount
    return stem


def compose():
    stems = {name: np.zeros((FRAMES, 2), dtype=np.float64)
             for name in ("arcane_atmosphere", "arcane_battle", "arcane_danger")}
    air, battle, danger = stems.values()
    kick, snare, tom, hat = percussion()
    chords = {
        "Dm": (50, 53, 57, 62), "Bb": (46, 50, 53, 58), "Gm": (43, 46, 50, 55),
        "A": (45, 49, 52, 57), "F": (41, 45, 48, 53), "C": (48, 52, 55, 60),
    }
    progression = (
        "Dm", "Dm", "Bb", "C", "Dm", "F", "Gm", "A",
        "Bb", "C", "Dm", "Dm", "Bb", "F", "Gm", "A",
        "Dm", "Bb", "F", "C", "Dm", "Bb", "Gm", "A",
    )
    # The middle phrase opens up the rhythm before the faster figure returns.
    running_strings = ((0, 0, .44), (.5, 2, .22), (.75, 3, .22), (1, 1, .44), (1.5, 2, .4),
                       (2, 0, .44), (2.5, 1, .22), (2.75, 2, .22), (3, 3, .44), (3.5, 2, .4))
    open_strings = ((0, 2, .44), (.5, 3, .4), (1, 1, .67), (1.75, 2, .22),
                    (2, 0, .44), (2.5, 2, .4), (3, 3, .44), (3.5, 1, .4))
    brass_calls = (
        ((0, 2, .7), (1.5, 3, .4), (2, 1, .86), (3.25, 2, .6)),
        ((.5, 3, .66), (1.5, 2, .42), (2.5, 1, .42), (3, 0, .82)),
    )
    for bar, harmony in enumerate(progression):
        chord = chords[harmony]
        beat = bar * 4
        figure = open_strings if 8 <= bar < 16 else running_strings
        for index, (step, degree, length) in enumerate(figure):
            strength = .22 if step % 1 == 0 else .15
            add(air, note(chord[degree] + 12, length * BEAT, "strings", strength),
                beat + step, .25 if index % 2 else -.25)
        for step in (0, 2):
            add(air, note(chord[0] - 12, 1.25 * BEAT, "bass", .22), beat + step)
            add(air, tom * .1, beat + step, -.15)
        for step in (.5, 1.5, 2.5, 3.5):
            add(air, hat * .07, beat + step, .3)

        for step, degree, length in brass_calls[bar % 2]:
            add(battle, note(chord[degree] + 12, length * BEAT, "brass", .22),
                beat + step, .14)
        for step, degree in ((.5, 0), (1.25, 0), (1.75, 2), (2.5, 0), (3.25, 0), (3.75, 2)):
            add(battle, note(chord[degree] - 12, .38 * BEAT, "bass", .18), beat + step)
        for step, strength in ((0, .33), (1.5, .21), (2, .29), (3.5, .2)):
            add(battle, kick * strength, beat + step)
        for step in (1, 3):
            add(battle, snare * .28, beat + step, -.18)

        for index, step in enumerate((.25, .5, .75, 1.25, 1.75, 2.25, 2.5, 2.75, 3.25, 3.75)):
            degree = (2, 1, 3, 2, 0)[index % 5]
            add(danger, note(chord[degree] + 24, .22 * BEAT, "strings", .12),
                beat + step, -.24 if index % 2 else .24)
        if bar % 4 == 3:
            for step, strength, pan in ((2.5, .16, -.3), (3.25, .19, .12), (3.75, .23, .3)):
                add(danger, tom * strength, beat + step, pan)
    reverb(air, .35)
    reverb(battle, .25)
    reverb(danger, .45)
    # Leave headroom at every blend of the three independently faded stems.
    positive_peak = np.max(sum(np.maximum(stem, 0) for stem in stems.values()))
    negative_peak = -np.min(sum(np.minimum(stem, 0) for stem in stems.values()))
    gain = .88 / max(positive_peak, negative_peak)
    return {name: stem * gain for name, stem in stems.items()}


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
        "title": "Pulso de batalla", "license": "CC0-1.0", "bpm": BPM,
        "bars": BARS, "beats_per_bar": 4, "key": "D minor", "sample_rate": RATE,
        "channels": 2, "tracks": tracks,
    }, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
