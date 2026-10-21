"""Migrate existing static armor charts in a saved, validated tank master."""
import json
import sys
from pathlib import Path
import bpy
sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog, fixture_selection, read_geometry, write_glb, write_json, require, sha
from tank import load_project, restore_normals
from tank_contract import compare as compare_rest
from roundtrip import export_scene
from tank_surface_contract import ASSETS, refine, compare


def run(request):
    repo = Path(__file__).resolve().parents[2]
    output = Path(request['outputRoot'])
    catalog = Catalog(request['inputRoot'])
    ids, families = fixture_selection(catalog, repo / 'tools/blender/fixtures/tank.json', [])
    blend = repo / 'assets/source/units/directorate/authoring/main_battle_tank.blend'
    baseline = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
                for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
    targets, counts = {}, {}
    for aid, geometry in baseline.items():
        targets[aid], counts[aid] = refine(aid, geometry, catalog.definitions[aid][1])
    require(all(counts[aid] > 0 for aid in ASSETS),
            'Expected existing weathered armor charts; migration already applied or source changed')
    load_project(catalog, ids, families, blend)
    restore_normals()
    export_scene(catalog, ids, families, baseline,
                 lambda aid, a, b: compare_rest(aid, a, b, catalog.definitions[aid][1]))
    for obj in bpy.context.scene.objects:
        if obj.get('fl_role') != 'geometry' or not counts[obj.get('fl_asset')]:
            continue
        primitive = targets[obj['fl_asset']]['primitives'][obj['fl_primitive']]
        for loop in obj.data.loops:
            index = obj.data.attributes['fl_vertex'].data[loop.vertex_index].value
            u, v = primitive['attributes']['TEXCOORD_0'][index]
            obj.data.uv_layers.active.data[loop.index].uv = (u, 1-v)
    export_scene(catalog, ids, families, baseline,
                 lambda aid, a, b: compare(aid, a, b, catalog.definitions[aid][1]))
    source = output / 'source'
    catalog.copy_to(ids, source)
    for aid, geometry in targets.items():
        if not counts[aid]:
            continue
        relative = catalog.source(aid).relative_to(catalog.root)
        write_glb(source / relative, geometry)
    candidate = Catalog(source)
    contract = json.loads(bpy.context.scene['fl_contract'])
    contract['sourceSignature'] = candidate.signature(ids)[0]
    bpy.context.scene['fl_contract'] = json.dumps(contract, sort_keys=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'main_battle_tank.blend'), check_existing=False)
    write_json(output / 'surface-refinement.json', {
        'success': True, 'baselineMasterSha256': sha(blend), 'repaintedVertices': counts,
        'sourceSignature': catalog.signature(ids)[0], 'candidateSignature': candidate.signature(ids)[0],
        'sharedTexturesChanged': False, 'geometryChanged': False,
        'policy': 'Static operational armor cell 8 to painted armor cell 0; damage and wear retained separately'})


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
