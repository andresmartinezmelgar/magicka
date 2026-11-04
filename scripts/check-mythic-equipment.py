#!/usr/bin/env python3
"""Comprueba el arte y la calibración sin compilar ni arrancar Godot. Requiere Pillow."""
import json
import math
import sys
from pathlib import Path

from PIL import Image


def check(root):
    client = root / 'src/Arcana.Client'
    assets = client / 'Assets/Wizards'
    spec = json.loads((assets / 'mythic-equipment.json').read_text())
    original = json.loads((assets / 'atlases.json').read_text())
    with Image.open(client / spec['Texture'].removeprefix('res://')) as image:
        image.verify()
    with Image.open(client / spec['Texture'].removeprefix('res://')) as image:
        assert image.mode == 'RGBA', 'El equipo necesita transparencia RGBA.'
        assert image.size == (spec['Width'], spec['Height']) == (1024, 1536)
        alpha = image.getchannel('A')
        assert alpha.getextrema()[0] == 0, 'El atlas carece de transparencia.'
        assert {s['Slot'] for s in spec['Sprites']} == set(range(6))
        assert len(spec['Sprites']) == 6
        for sprite in spec['Sprites']:
            assert len(sprite['Views']) == 4
            for column, region in enumerate(sprite['Views']):
                x, y, w, h = (region[k] for k in ('X', 'Y', 'Width', 'Height'))
                assert w > 0 and h > 0 and x >= 0 and y >= 0
                assert x+w <= image.width and y+h <= image.height
                assert x//256 == (x+w-1)//256 == column, 'La región cruza vistas.'
                assert y//256 == (y+h-1)//256, 'La región cruza piezas.'
                pixels = alpha.crop((x, y, x+w, y+h))
                coverage = pixels.point(lambda a: 255 if a >= 40 else 0)
                assert coverage.getbbox() is not None, 'Vista sin arte visible.'
                assert coverage.histogram()[255] > w*h*.15, 'Vista casi vacía.'
                assert max(pixels.getpixel((cx, cy)) for cx, cy in ((0, 0), (w-1, 0), (0, h-1), (w-1, h-1))) < 26, 'El arte toca una esquina del recorte.'
    assert len(spec['Rigs']) == len(original) == 5
    assert {r['Type'] for r in spec['Rigs']} == {a['Type'] for a in original}
    poses = bones = 0
    keys = ('Hat', 'Chest', 'LeftBoot', 'RightBoot', 'Weapon', 'GoldRing', 'MagicRing')
    for rig in spec['Rigs']:
        atlas = next(a for a in original if a['Type'] == rig['Type'])
        with Image.open(client / atlas['Texture'].removeprefix('res://')) as body:
            body_alpha = body.getchannel('A')
        assert len(rig['Frames']) == len(atlas['Frames']) == 16
        for index, pose in enumerate(rig['Frames']):
            poses += 1
            assert all(k in pose for k in keys)
            beard = pose['Beard']
            assert len(beard) == 4 and all(math.isfinite(v) for v in beard)
            assert beard[2] >= 0 and beard[3] >= 0
            assert (beard[2] == 0) == (index//4 == 2), 'La barba no corresponde a la orientación.'
            assert (pose['Weapon'] is None) == (index%4 == 2), 'El báculo no coincide con la pose original.'
            for key in keys:
                bone = pose[key]
                if bone is None:
                    assert key == 'Weapon'
                    continue
                bones += 1
                assert all(math.isfinite(v) for v in bone.values())
                x, y, w, h = (bone[k] for k in ('X', 'Y', 'Width', 'Height'))
                assert 0 <= x < atlas['Width']/4 and 0 <= y < atlas['Height']/4
                assert w > 0 and h > 0
                angle = math.radians(bone.get('Angle', 0))
                px, py = bone.get('PivotX', .5), bone.get('PivotY', .5)
                assert 0 <= px <= 1 and 0 <= py <= 1
                slot = {'Hat': 1, 'Chest': 2, 'LeftBoot': 0, 'RightBoot': 0, 'Weapon': 3, 'GoldRing': 4, 'MagicRing': 5}[key]
                region = next(s for s in spec['Sprites'] if s['Slot'] == slot)['Views'][index//4]
                assert math.isclose(w/h, region['Width']/region['Height'], rel_tol=1e-5), f'Arte deformado: {rig["Type"]}/{index}/{key}'
                # Comprueba las cuatro esquinas alrededor del apoyo real, también al girar.
                for u, v in ((0, 0), (0, 1), (1, 0), (1, 1)):
                    dx, dy = (u-px)*w, (v-py)*h
                    vx, vy = x+dx*math.cos(angle)-dy*math.sin(angle), y+dx*math.sin(angle)+dy*math.cos(angle)
                    assert 0 <= vx <= atlas['Width']/4 and 0 <= vy <= atlas['Height']/4, f'Pieza fuera de pose: {rig["Type"]}/{index}/{key}'
                if slot == 0: assert py >= .9, 'La bota debe sujetarse por la suela.'
                if slot == 1: assert py >= .9, 'La corona debe sujetarse por la banda.'
                if slot == 3: assert py >= .9, 'El remate debe sujetarse por el collar de la vara.'
                if bone.get('Visible', True):
                    ax, ay = int(index%4*atlas['Width']/4+x), int(index//4*atlas['Height']/4+y)
                    contact = body_alpha.crop((ax-8, ay-8, ax+9, ay+9))
                    assert contact.getextrema()[1] >= 160, f'Pieza sin apoyo sobre el cuerpo: {rig["Type"]}/{index}/{key}'
    presets = (client / 'export_presets.cfg').read_text()
    filters = [line for line in presets.splitlines() if line.startswith('include_filter=')]
    assert len(filters) == 2 and all('Assets/Wizards/mythic-equipment.json' in f for f in filters), 'Falta el índice en una exportación.'
    imports = (assets / 'mythic-equipment.png.import').read_text()
    assert 'mipmaps/generate=true' in imports and 'process/fix_alpha_border=true' in imports
    assert 'compress/mode=0' in imports
    print(f'MYTHIC_ASSETS_OK: 24 vistas, {poses} poses, {bones} anclajes, alfa, recortes, importación y Windows/Android.')


if __name__ == '__main__':
    check(Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1])
