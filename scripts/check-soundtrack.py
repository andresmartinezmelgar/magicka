#!/usr/bin/env python3
"""Check the shipped audio without building or starting Godot (NumPy + FFmpeg)."""
from pathlib import Path
import hashlib
import json
import subprocess

import numpy as np


def main():
    folder = Path(__file__).resolve().parents[1] / "src/Arcana.Client/Assets/Audio"
    metadata = json.loads((folder / "soundtrack.json").read_text(encoding="utf-8"))
    expected_frames = round(metadata["bars"] * metadata["beats_per_bar"] * 60 / metadata["bpm"] * metadata["sample_rate"])
    decoded = []
    for entry in metadata["tracks"]:
        path = folder / entry["file"]
        assert hashlib.sha256(path.read_bytes()).hexdigest() == entry["sha256"], f"Hash: {path.name}"
        probe = json.loads(subprocess.check_output([
            "ffprobe", "-v", "error", "-show_streams", "-of", "json", str(path)]))["streams"][0]
        assert probe["codec_name"] == "vorbis", f"Codec: {path.name}"
        assert int(probe["sample_rate"]) == metadata["sample_rate"], f"Rate: {path.name}"
        assert probe["channels"] == metadata["channels"] == 2, f"Stereo: {path.name}"
        raw = subprocess.check_output(["ffmpeg", "-v", "error", "-i", str(path), "-f", "f32le", "-"])
        pcm = np.frombuffer(raw, dtype="<f4").reshape(-1, 2)
        assert len(pcm) == entry["frames"] == expected_frames, f"Loop length: {path.name} ({len(pcm)})"
        assert np.isfinite(pcm).all(), f"Non-finite samples: {path.name}"
        peak = float(np.max(np.abs(pcm)))
        rms = float(np.sqrt(np.mean(pcm ** 2)))
        assert .005 < rms and peak < .95, f"Silence/clipping: {path.name}"
        jump = float(np.max(np.abs(pcm[-1] - pcm[0])))
        assert jump < .08, f"Loop seam: {path.name} ({jump:.4f})"
        decoded.append(pcm)
        print(f"PASS {path.name}: {len(pcm)} frames, peak={peak:.3f}, RMS={rms:.3f}, seam={jump:.4f}")
    # Peak of the loudest possible unattenuated music mix, before the master gain.
    combined = sum(decoded)
    assert float(np.max(np.abs(combined))) < .98, "The combined stems clip."
    print(f"PASS synchronized mix: peak={np.max(np.abs(combined)):.3f}; all three loops have identical duration")


if __name__ == "__main__":
    main()
