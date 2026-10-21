"""Reject unsupported artillery rest edits with actual Blender, then prove a valid reopen."""
import json
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, fixture_selection, read_geometry, require, write_json
from roundtrip import export_scene
from mechanical import author_definitions

request = json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig'))
blend, fixture = map(Path, request['arguments'])
catalog = Catalog(request['inputRoot'])
ids, families = fixture_selection(catalog, fixture, [])
geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
              for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
rejected = []


def part(kind):
    return next(obj for obj in bpy.context.scene.objects if obj.get('fl_motion') == kind)


def validate():
    export_scene(catalog, ids, families, geometries)
    updates = author_definitions(catalog, geometries, {'maxPivotDeltaMetres': 0.025})
    require(all(value == catalog.definitions[aid][1] for aid, value in updates.items()), 'Preservation metadata changed')


def reject(name, change):
    bpy.ops.wm.open_mainfile(filepath=str(blend))
    change()
    try:
        validate()
    except (ValueError, KeyError):
        rejected.append(name)
        return
    raise AssertionError('Unsupported edit accepted: ' + name)


reject('unknown motion', lambda: part('wheel').__setitem__('fl_motion', 'skeletal'))
reject('unbounded rate', lambda: part('wheel').__setitem__('fl_rate', 21))
reject('changed bounded rate', lambda: part('wheel').__setitem__('fl_rate', -1.75))
reject('missing motion property', lambda: part('wheel').__delitem__('fl_rate'))
reject('unbaked frame transform', lambda: setattr(part('wheel'), 'location', (10, 0, 0)))
reject('arbitrary animation', lambda: part('wheel').keyframe_insert(data_path='location', frame=1))
reject('locked cannon pivot', lambda: part('cannon').__setitem__('fl_pivot_game', [0.01, 0.24, -0.04]))
reject('gameplay muzzle socket', lambda: setattr(next(o for o in bpy.context.scene.objects
       if o.get('fl_role') == 'socket' and o.get('fl_logical_name') == 'weapon_muzzle'), 'location', (2, 3, 4)))
bpy.ops.wm.open_mainfile(filepath=str(blend))
validate()
write_json(Path(request['outputRoot']) / 'native-contracts.json', {'success': True,
           'rejected': rejected, 'unchangedRestAccepted': True})
