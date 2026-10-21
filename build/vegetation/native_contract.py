"""Exercise vegetation-master rejection with the actual pinned Blender process."""
import json
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog
from asset_contract import fixture_selection, read_geometry, write_json
from roundtrip import export_scene

request = json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig'))
blend, fixture, root = request['arguments']
catalog = Catalog(request['inputRoot'])
ids, families = fixture_selection(catalog, fixture, [root])
geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
              for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
rejected = []


def mesh():
    return next(o for o in bpy.context.scene.objects if o.get('fl_role') == 'geometry' and o.get('fl_asset') == root)


def reject(name, change):
    bpy.ops.wm.open_mainfile(filepath=str(Path(blend).resolve()))
    change()
    try:
        export_scene(catalog, ids, families, geometries)
    except (ValueError, KeyError):
        rejected.append(name)
        return
    raise AssertionError('Unsupported edit accepted: ' + name)


reject('unbaked transform', lambda: setattr(mesh(), 'location', (1, 0, 0)))
reject('changed envelope', lambda: setattr(mesh().data.vertices[0], 'co', (9, 9, 9)))
reject('modifier', lambda: mesh().modifiers.new('Subdivision', 'SUBSURF'))
reject('animation', lambda: mesh().keyframe_insert(data_path='location', frame=1))
reject('material slots', lambda: mesh().data.materials.clear())
reject('unexpected object', lambda: bpy.ops.mesh.primitive_cube_add())
bpy.ops.wm.open_mainfile(filepath=str(Path(blend).resolve()))
export_scene(catalog, ids, families, geometries)
write_json(Path(request['outputRoot']) / 'native-contracts.json',
           {'success': True, 'root': root, 'rejected': rejected, 'unchangedReopenAccepted': True})
