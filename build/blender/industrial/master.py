"""Organize a preserved family master and prove a saved vertex edit is exportable."""
import copy
import json
import sys
from pathlib import Path

import bpy

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / 'tools/blender'))
from asset_contract import Catalog, compare_geometry, fixture_selection, read_geometry, require, write_glb, write_json
from roundtrip import export_scene


def run(request):
    output = Path(request['outputRoot'])
    project, fixture = map(Path, request['arguments'])
    catalog = Catalog(request['inputRoot'])
    ids, families = fixture_selection(catalog, fixture, [])
    require(len(families) == 1, 'One independently editable family per master')
    root = families[0]['root']
    geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
                  for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
    bpy.ops.wm.open_mainfile(filepath=str(project.resolve()))
    for aid in sorted(geometries):
        collection = bpy.data.collections.new(aid)
        bpy.context.scene.collection.children.link(collection)
        for obj in list(bpy.context.scene.objects):
            if obj.get('fl_asset') == aid:
                for previous in list(obj.users_collection):
                    previous.objects.unlink(obj)
                collection.objects.link(obj)
                obj.hide_set(aid != root)
                obj.hide_render = aid != root
    # Collection visibility is editorial only; all original state geometry still exports.
    export_scene(catalog, ids, families, geometries)
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'authoring.blend'), check_existing=False)
    bpy.ops.wm.open_mainfile(filepath=str(output / 'authoring.blend'))
    export_scene(catalog, ids, families, geometries)
    obj = next(o for o in bpy.context.scene.objects
               if o.get('fl_role') == 'geometry' and o.get('fl_asset') == root)
    vertex = max(obj.data.vertices, key=lambda v: v.co.z)
    source_vertex = obj.data.attributes['fl_vertex'].data[vertex.index].value
    primitive = obj['fl_primitive']
    vertex.co.z += .001
    expected = copy.deepcopy(geometries)
    position = list(expected[root]['primitives'][primitive]['attributes']['POSITION'][source_vertex])
    position[1] += .001
    expected[root]['primitives'][primitive]['attributes']['POSITION'][source_vertex] = position
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'edit-test.blend'), check_existing=False)
    bpy.ops.wm.open_mainfile(filepath=str(output / 'edit-test.blend'))
    results, _, _ = export_scene(catalog, ids, families, geometries,
                                geometry_comparison=lambda aid, before, after: compare_geometry(expected[aid], after))
    actual = results[root]['primitives'][primitive]['attributes']['POSITION'][source_vertex]
    require(abs(actual[1] - geometries[root]['primitives'][primitive]['attributes']['POSITION'][source_vertex][1]) > .0009,
            'Saved edit did not reach the exported geometry')
    source = output / 'edit-source'
    catalog.copy_to(ids, source)
    for aid, geometry in results.items():
        path, definition = catalog.definitions[aid]
        relative = path.relative_to(catalog.root)
        target = Path('_edited') / (aid + '.glb')
        write_glb(source / relative.parent / target, geometry)
        updated = copy.deepcopy(definition)
        updated['source'] = target.as_posix()
        write_json(source / relative, updated)
    write_json(output / 'master-report.json', {
        'schemaVersion': 1, 'success': True, 'family': root,
        'sourceSignature': catalog.signature(ids)[0], 'meshCount': len(geometries),
        'organizedMasterReopened': True, 'savedVertexEditReopenedAndExported': True,
        'editAsset': root, 'editVertex': source_vertex, 'canonicalYDelta': actual[1] - position[1] + .001,
        'editIsQualificationOnly': True, 'productionPromotion': 'pending'
    })


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
