"""Partition existing production meshes into verified rigid triangle ranges.

Run after Author-UnitRunningGear.py. Original sources remain untouched. No
vertices, UVs, material identities or triangle winding are changed.
"""
import base64
import json
from pathlib import Path
from importlib.machinery import SourceFileLoader
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
reader = SourceFileLoader('mechanical_reader', str(Path(__file__).with_name('Refine-ProductionMeshes.py'))).load_module()


def load(path):
    g = json.loads(path.read_text())
    raw = base64.b64decode(g['buffers'][0]['uri'].split(',')[1])
    p = g['meshes'][0]['primitives'][0]
    return g, raw, reader.read(g, raw, p['attributes']['POSITION']), reader.read(g, raw, p['attributes']['TEXCOORD_0']), reader.read(g, raw, p['indices']).ravel()


def author(meta):
    d = json.loads(meta.read_text())
    if '/combat_support/' in d['source'].replace('\\', '/'): return
    if d['id'].startswith('unit.directorate.main_battle_tank') or d['source'].replace('\\','/').startswith('sources/operational/') or '/infantry/' in d['source'].replace('\\','/') or '/battlefield/' in d['source'].replace('\\','/'):
        return  # Dedicated editable tank authoring owns its rigid ranges.
    path = (meta.parent / d['source']).resolve()
    if path.stem.endswith('_detailed'):
        path = path.with_name(path.stem.removesuffix('_detailed') + '_production.gltf')
    if path.stem.endswith('_motion'):
        path = path.with_name(path.stem.removesuffix('_motion') + '.gltf')
    original = path.with_name(path.stem.removesuffix('_production') + '.gltf')
    _, _, original_pos, original_uv, _ = load(original)
    g, raw, pos, _, indices = load(path)
    static = []
    moving = []
    cursor = 0
    sockets = [s for s in d.get('sockets', []) if not s['name'].startswith('motion_')]
    is_unit = d['id'].startswith('unit.')

    def add(name, kind, part_indices, pivot, rate=1, travel=0):
        points = pos[np.unique(part_indices)]
        assert np.isfinite(points).all() and np.isfinite(pivot).all()
        # Every running-gear pivot must lie inside the actual component bounds.
        if kind == 'wheel':
            assert np.all(pivot >= points.min(0) - 1e-5) and np.all(pivot <= points.max(0) + 1e-5)
        socket = 'motion_' + name
        sockets.append(dict(name=socket, x=float(pivot[0]), y=float(pivot[1]), z=float(pivot[2])))
        moving.append((dict(name=name, parent=-1, motion=kind, socket=socket, rate=rate, travel=travel), part_indices))

    for component in range(len(original_pos) // 24):
        op = original_pos[component * 24:component * 24 + 24]
        lo, hi = op.min(0), op.max(0)
        extent, center = hi - lo, (hi + lo) / 2
        uv = original_uv[component * 24:component * 24 + 24].mean(0)
        cell = int(uv[1] * 4) * 4 + int(uv[0] * 4)
        wheel = is_unit and cell == 2
        track = wheel and extent[2] > extent[1] * 2
        barrel = is_unit and cell == 7 and extent[2] > max(extent[0], extent[1]) * 2
        count = 912 if track else 192 if wheel else 384 if barrel else 132
        part = indices[cursor:cursor + count]
        assert len(part) == count
        points = pos[np.unique(part)]
        assert np.allclose(points.min(0), lo, atol=1e-5) and np.allclose(points.max(0), hi, atol=1e-5), (d['id'], component)
        cursor += count
        if track:
            static.extend(part[:144])
            for n in range(4):
                wheel_part = part[144 + n * 192:144 + (n + 1) * 192]
                wp = pos[np.unique(wheel_part)]
                add(f'road_wheel_{component}_{n}', 'wheel', wheel_part, (wp.min(0) + wp.max(0)) / 2, rate=1.5)
        elif wheel:
            add(f'wheel_{component}', 'wheel', part, center, rate=1.5)
        elif barrel:
            add('cannon', 'cannon', part, np.array([0, .16, .1]), travel=.045)
        elif 'scout_vehicle' in d['id'] and component == 9:
            add('sensor', 'radar', part, center, rate=.6)
        elif 'vehicle_factory' in d['id'] and component == 3:
            add('loading_door', 'door', part, center, travel=.27)
        elif 'vehicle_factory' in d['id'] and component == 5:
            add('overhead_crane', 'crane', part, np.array([0, .24, -.05]), rate=.25, travel=.25)
        else:
            static.extend(part)
    assert cursor == len(indices)
    reordered = list(static)
    parts = [dict(name='body', firstIndex=0, indexCount=len(static), parent=-1, motion='static')]
    for definition, part_indices in moving:
        definition.update(firstIndex=len(reordered), indexCount=len(part_indices))
        parts.append(definition)
        reordered.extend(part_indices)
    assert sorted(reordered) == sorted(indices.tolist())
    assert len(parts) <= 32
    accessor = g['accessors'][g['meshes'][0]['primitives'][0]['indices']]
    view = g['bufferViews'][accessor['bufferView']]
    offset = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    payload = np.asarray(reordered, dtype='<u4').tobytes()
    raw = raw[:offset] + payload + raw[offset + len(payload):]
    g['buffers'][0]['uri'] = 'data:application/octet-stream;base64,' + base64.b64encode(raw).decode()
    dest = path.with_name(path.stem + '_motion.gltf')
    dest.write_text(json.dumps(g, indent=2) + '\n')
    d['source'] = str(Path(d['source']).with_name(dest.name)).replace('\\', '/')
    d['sockets'], d['mechanicalParts'] = sockets, parts
    meta.write_text(json.dumps(d, indent=2) + '\n')
    print(d['id'], len(parts), 'parts;', len(indices) // 3, 'unchanged triangles', flush=True)


if __name__ == '__main__':
    for relative in (
        'units/directorate/vehicles/main_battle_tank.asset.json',
        'units/directorate/vehicles/main_battle_tank_turret.asset.json',
        'units/directorate/vehicles/scout_vehicle.asset.json',
        'units/directorate/logistics/cargo_truck.asset.json',
        'units/directorate/logistics/supply_truck.asset.json',
        'buildings/directorate/vehicle_factory.asset.json',
    ):
        author(ROOT / 'assets/source' / relative)
