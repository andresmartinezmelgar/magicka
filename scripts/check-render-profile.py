#!/usr/bin/env python3
"""Valida medidas reales y su procedencia sin imponer un objetivo de FPS a CI."""
import json
from pathlib import Path
import sys

for filename in sys.argv[1:]:
    report = json.loads(Path(filename).read_text())
    assert report['schema'] == 1 and not report['canceled']
    assert report['scope'] == 'synthetic_local_render_scene'
    assert report['samples'] >= 3 and report['measuredSeconds'] > 0 and report['averageFps'] > 0
    assert report['scene'] == dict(players=16, projectiles=128, walls=40, fields=40, beams=64, eventsPerFrame=160)
    assert report['device']['gpu'] and report['device']['windowWidth'] > 0 and report['device']['windowHeight'] > 0
    for name in ('frameMs', 'clientCpuMs', 'drawCalls', 'primitives', 'managedBytes', 'nodes'):
        value = report['metrics'][name]
        assert value['samples'] == report['samples'] and value['mean'] > 0 and value['maximum'] >= value['p95'] >= value['p50']
    for name in ('gpuMs', 'renderCpuMs', 'staticBytes', 'videoBytes'):
        value = report['metrics'][name]
        assert value is None or value['mean'] > 0
    assert report['peakActiveParticles'] <= report['particleLimit']
    assert report['peakActiveRings'] <= report['ringLimit']
    print(f"RENDER_PROFILE_OK: {report['map']}, {report['samples']} muestras, {report['averageFps']:.2f} FPS, {report['environment']}, {report['device']['gpu']}")
