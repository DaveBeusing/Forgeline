"""Prove a saved, rooted native vertex edit survives export and compilation."""
import copy
from pathlib import Path
import bpy
from asset_contract import Catalog, compare_geometry, fixture_selection, read_geometry, require, write_glb, write_json
from roundtrip import export_scene


def run(request):
    output = Path(request['outputRoot'])
    project, fixture = map(Path, request['arguments'])
    catalog = Catalog(request['inputRoot'])
    ids, families = fixture_selection(catalog, fixture, [])
    require(len(families) == 1, 'One family per editable master')
    root = families[0]['root']
    geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
    bpy.ops.wm.open_mainfile(filepath=str(project.resolve()))
    export_scene(catalog, ids, families, geometries)
    obj = next(o for o in bpy.context.scene.objects if o.get('fl_role') == 'geometry' and o.get('fl_asset') == root)
    vertex = max(obj.data.vertices, key=lambda v: v.co.z)
    source_vertex = obj.data.attributes['fl_vertex'].data[vertex.index].value
    primitive = obj['fl_primitive']
    vertex.co.x += .001
    # A geometry edit changes Blender's encoded split-normal basis. Reapply the
    # authored normals explicitly and retain the resulting preview encoding.
    obj.data.update()
    obj.data.normals_split_custom_set([tuple(a.vector) for a in obj.data.attributes['fl_normal'].data])
    obj.data.update()
    for loop in obj.data.loops:
        obj.data.attributes['fl_normal_preview'].data[loop.index].vector = obj.data.corner_normals[loop.index].vector
    expected = copy.deepcopy(geometries)
    position = list(expected[root]['primitives'][primitive]['attributes']['POSITION'][source_vertex])
    position[0] += .001
    expected[root]['primitives'][primitive]['attributes']['POSITION'][source_vertex] = position
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'edit-test.blend'), check_existing=False)
    bpy.ops.wm.open_mainfile(filepath=str(output / 'edit-test.blend'))
    results, _, _ = export_scene(catalog, ids, families, geometries, geometry_comparison=lambda aid, before, after: compare_geometry(expected[aid], after))
    actual = results[root]['primitives'][primitive]['attributes']['POSITION'][source_vertex]
    delta = actual[0] - geometries[root]['primitives'][primitive]['attributes']['POSITION'][source_vertex][0]
    require(.0009 < delta < .0011, 'Saved native edit did not reach export')
    source = output / 'edit-source'
    catalog.copy_to(ids, source)
    for aid, geometry in results.items():
        path, definition = catalog.definitions[aid]
        relative = path.relative_to(catalog.root)
        target = Path('_edited') / (path.stem + '.glb')
        write_glb(source / relative.parent / target, geometry)
        updated = copy.deepcopy(definition)
        updated['source'] = target.as_posix()
        write_json(source / relative, updated)
    write_json(output / 'master-report.json', {'schemaVersion': 1, 'success': True, 'family': root, 'sourceSignature': catalog.signature(ids)[0], 'savedVertexEditReopenedAndExported': True, 'canonicalXDelta': delta, 'rootYUnchanged': True, 'editIsQualificationOnly': True})
