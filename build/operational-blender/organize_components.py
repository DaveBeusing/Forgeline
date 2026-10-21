"""Retain source component selection groups without changing geometry or rigid contracts."""
import json
import sys
from pathlib import Path
import bpy
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, fixture_selection, read_geometry, require, sha, write_glb, write_json
from roundtrip import export_scene

def organize(request):
    qualified = Path(request['arguments'][0])
    output = Path(request['outputRoot'])
    catalog = Catalog(request['inputRoot'])
    records = []
    allowed = ('vehicle_factory', 'power_plant', 'barracks', 'radar')
    selected = request['arguments'][1:] or allowed
    require(len(set(selected)) == len(selected) and all(x in allowed for x in selected), 'Invalid family selection')
    for family in selected:
        fixture = Path(__file__).with_name(family + '.json')
        ids, families = fixture_selection(catalog, fixture, [])
        geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
            for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
        original = qualified / family / 'master/authoring.blend'
        bpy.ops.wm.open_mainfile(filepath=str(original))
        groups = []
        for aid in families[0]['assets']:
            source = catalog.source(aid)
            require(source.suffix == '.gltf', 'Expected the retained operational component source')
            components = json.loads(source.read_text(encoding='utf-8-sig'))['extras']['componentHierarchy']
            for obj in bpy.context.scene.objects:
                if obj.get('fl_asset') != aid or obj.get('fl_role') != 'geometry':
                    continue
                primitive = geometries[aid]['primitives'][obj['fl_primitive']]
                identities = obj.data.attributes['fl_vertex'].data
                for component in components:
                    start, count = component['firstIndex'], component['indexCount']
                    selected = set(primitive['indices'][start:start + count])
                    vertices = [v.index for v in obj.data.vertices if identities[v.index].value in selected]
                    if not vertices:
                        continue
                    name = component['module'] + '/' + component['part']
                    require(len(name.encode('utf-8')) <= 63, 'Component group name exceeds Blender limit')
                    group = obj.vertex_groups.new(name=name)
                    require(group.name == name, 'Component group name changed')
                    group.add(vertices, 1.0, 'REPLACE')
                    groups.append({'asset': aid, 'object': obj.name, 'group': name, 'vertices': len(vertices)})
        require({x['asset'] for x in groups} == set(families[0]['assets']), 'Incomplete state/LOD component selection groups')
        target = output / (family + '.blend')
        bpy.context.scene['fl_component_selection'] = 'Vertex groups retain original kit module/part names; runtime rigid ranges are unchanged.'
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(target), check_existing=False)
        bpy.ops.wm.open_mainfile(filepath=str(target))
        results, _, _ = export_scene(catalog, ids, families, geometries)
        exported = Catalog(qualified / family / 'first/source')
        for aid in families[0]['assets']:
            glb = output / family / (aid + '.glb')
            write_glb(glb, results[aid])
            require(sha(glb) == sha(exported.source(aid)), 'Component organization changed exported geometry')
        records.append({'family': family, 'sourceSignature': catalog.signature(ids)[0],
            'originalMasterSha256': sha(original), 'masterSha256': sha(target),
            'reopened': True, 'exactExports': 10, 'componentGroups': groups})
    write_json(output / 'component-masters.json', {'success': True, 'families': records})

if __name__ == '__main__':
    organize(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
