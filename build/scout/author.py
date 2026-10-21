"""Preserve the integrated scout kit using the existing strict Blender adapter."""
import argparse
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, compare_geometry, fixture_selection, geometry_stats, read_geometry, require, sha, write_glb, write_json
import roundtrip
import mechanical

# Family-only samples of existing copied rigid inputs, not runtime animation clips.
mechanical.SAMPLES = [
    {'name': 'idle'},
    {'name': 'forward', 'travel': 1.7, 'active': 2},
    {'name': 'reverse', 'travel': -1.7, 'active': 2},
    {'name': 'stopped', 'travel': 1.7, 'active': 2, 'seconds': 20},
    {'name': 'paused', 'travel': 1.7, 'active': 2},
    {'name': 'firing', 'active': 2, 'shot': 1.95, 'aim': .6, 'elevation': .2},
    {'name': 'recovery', 'active': 2, 'shot': 1.85, 'aim': .6, 'elevation': .2},
    {'name': 'sensor-still', 'travel': 1.7},
    {'name': 'sensor-active', 'active': 2},
    {'name': 'destroyed', 'active': 2, 'disabled': True},
    {'name': 'lod', 'active': 2, 'weight': 0},
]


def preserve_source_floats(catalog, geometries, output):
    # A preservation master must not turn editor float storage noise into new art.
    # The shared exporter has already validated the actual Blender objects/frames.
    exported = Catalog(output / 'source')
    report_path = output / 'roundtrip-report.json'
    raw_report = report_path.read_bytes()
    (output / 'native-readback-report.json').write_bytes(raw_report)
    report = json.loads(raw_report)
    backups = output / 'native-readback-glb'
    backups.mkdir()
    records = []
    for aid, original in sorted(geometries.items()):
        path = exported.source(aid)
        definition = catalog.definitions[aid][1]
        observed = read_geometry(path, exported.root, definition)
        deviations = compare_geometry(original, observed)
        require(max(deviations.values()) <= 5e-8,
                'Scout preservation export exceeds editor float storage noise; review source-kit edits explicitly')
        backup = backups / (aid + '.glb')
        shutil.copyfile(path, backup)
        write_glb(path, original)
        final = read_geometry(path, exported.root, definition)
        exact = compare_geometry(original, final)
        require(max(exact.values()) == 0, 'Source vertex channels were not preserved exactly')
        row = next(r for r in report['meshes'] if r['id'] == aid)
        row['nativeReadbackSha256'] = sha(backup)
        row['nativeReadbackMaximumDeviations'] = row['maximumDeviations']
        row['exportSha256'] = sha(path)
        row['exportStats'] = geometry_stats(final)
        row['maximumDeviations'] = exact
        records.append({'id': aid, 'nativeReadback': backup.relative_to(output).as_posix(),
                        'nativeReadbackSha256': sha(backup), 'nativeReadbackDeviations': deviations,
                        'sourceFloatExportSha256': sha(path), 'finalDeviations': exact})
    report['canonicalSourceFloats'] = True
    write_json(report_path, report)
    write_json(output / 'canonical-export-report.json', {'schemaVersion': 1,
        'nativeSceneValidatedBeforeSerialization': True, 'maximumEditorStorageNoise': 5e-8,
        'sourceAttributesExact': True, 'assets': records})


def component_groups(catalog, geometries):
    groups = []
    for aid, geometry in geometries.items():
        source = catalog.source(aid)
        components = json.loads(source.read_text(encoding='utf-8-sig')).get('extras', {}).get('componentHierarchy', []) if source.suffix == '.gltf' else []
        for obj in bpy.context.scene.objects:
            if obj.get('fl_role') != 'geometry' or obj.get('fl_asset') != aid:
                continue
            primitive = geometry['primitives'][obj['fl_primitive']]
            identities = obj.data.attributes['fl_vertex'].data
            for component in components:
                selected = set(primitive['indices'][component['firstIndex']:component['firstIndex'] + component['indexCount']])
                vertices = [v.index for v in obj.data.vertices if identities[v.index].value in selected]
                if vertices:
                    group = obj.vertex_groups.new(name=component['name'])
                    require(group.name == component['name'], 'Scout component group name changed')
                    group.add(vertices, 1.0, 'REPLACE')
                    groups.append({'asset': aid, 'group': group.name, 'vertices': len(vertices)})
    require({x['asset'] for x in groups} == {aid for aid in geometries if not aid.endswith('.collision')}, 'Visual component groups incomplete')
    bpy.context.scene['fl_component_selection'] = 'Original source kit module names are vertex selection groups; runtime partitions remain authoritative.'
    return groups


def inspection(catalog, ids, families, geometries, output, render):
    roundtrip.import_scene(catalog, ids, families, geometries)
    scene = bpy.context.scene
    scene.render.fps = 20
    scene.frame_start = 1
    scene.frame_end = len(mechanical.SAMPLES) * 10
    # Fully composed world poses already contain parent motion. Flatten only the
    # generated inspection frames: a nonuniform parent plus child rotation would
    # otherwise require shear in Blender's local TRS decomposition.
    for obj in scene.objects:
        if obj.get('fl_role') == 'part':
            world = obj.matrix_world.copy()
            obj.parent = None
            obj.matrix_world = world
    rows = []
    for index, sample in enumerate(mechanical.SAMPLES):
        frame = 1 + index * 10
        scene.frame_set(frame)
        for aid in sorted(geometries):
            definition = catalog.definitions[aid][1]
            parts = definition.get('mechanicalParts', [])
            if not parts:
                continue
            family = next(f for f in families if aid in f['assets'])
            scale = family['instanceScale']
            world = Matrix.Translation(family['instanceTranslation']) @ Matrix.Rotation(
                math.radians(family['instanceYawDegrees']), 4, 'Y') @ Matrix.Diagonal((*scale, 1))
            if aid == family['root'] + '.turret':
                socket = next(s for s in catalog.definitions[family['root']][1]['sockets'] if s['name'] == 'turret_pivot')
                pivot = Vector([socket[k] for k in 'xyz'])
                scaling = Matrix.Diagonal((*scale, 1))
                world = world @ Matrix.Translation(pivot) @ scaling.inverted() @ Matrix.Rotation(
                    sample.get('aim', 0), 4, 'Y') @ scaling @ Matrix.Translation(-pivot)
            transforms = mechanical.compose(parts, definition, sample, world, scale)
            frames = {obj['fl_index']: obj for obj in scene.objects
                      if obj.get('fl_role') == 'part' and obj.get('fl_asset') == aid}
            actual = []
            for part_index, matrix in enumerate(transforms):
                obj = frames[part_index]
                pivot = Vector(roundtrip.pivot_of(definition, parts[part_index]))
                obj.rotation_mode = 'QUATERNION'
                obj.matrix_world = roundtrip.G2B @ matrix @ Matrix.Translation(pivot) @ roundtrip.G2B.inverted()
                bpy.context.view_layer.update()
                observed = roundtrip.G2B.inverted() @ obj.matrix_world @ roundtrip.G2B @ Matrix.Translation(-pivot)
                require(max(abs(matrix[r][c] - observed[r][c]) for r in range(4) for c in range(4)) <= 2e-5,
                        'Scout inspection exceeds the existing runtime matrix tolerance')
                actual.append([observed[r][c] for c in range(4) for r in range(4)])
                for prop in ('location', 'rotation_quaternion', 'scale'):
                    obj.keyframe_insert(data_path=prop, frame=frame)
            rows.append({'id': aid, 'root': family['root'], 'frame': frame, 'sample': sample, 'matrices': actual})
    mechanical.layers(families)
    images = mechanical.preview_images(catalog, ids, families, output, render)
    write_json(output / 'poses.json', {'schemaVersion': 1, 'families': families, 'samples': rows})
    return len(rows), images


def run(request):
    parser = argparse.ArgumentParser()
    parser.add_argument('--fixtures', required=True, type=Path)
    parser.add_argument('--action', choices=['RoundTrip', 'Export'], default='RoundTrip')
    parser.add_argument('--blend-file', type=Path)
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args(request['arguments'])
    forwarded = ['--fixtures', str(args.fixtures), '--action', args.action]
    if args.blend_file:
        forwarded += ['--blend-file', str(args.blend_file)]
    roundtrip.run(dict(request, arguments=forwarded))
    output = Path(request['outputRoot'])
    catalog = Catalog(request['inputRoot'])
    ids, families = fixture_selection(catalog, args.fixtures, [])
    geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
                  for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
    preserve_source_floats(catalog, geometries, output)
    # The rest project remains a strict preservation input. Generated actions and
    # packed PBR graphs belong to the separate inspection project only.
    if args.action != 'Export':
        mechanical.add_properties(catalog)
    updates = mechanical.author_definitions(catalog, geometries, {'maxPivotDeltaMetres': 0.025})
    require(all(value == catalog.definitions[aid][1] for aid, value in updates.items()),
            'Preservation master cannot change mechanical metadata; use the established motion authoring path')
    groups = component_groups(catalog, geometries) if args.action != 'Export' else []
    if args.action != 'Export':
        mechanical.layers(families)
        bpy.ops.wm.save_as_mainfile(filepath=str(output / 'authoring.blend'), check_existing=False)
    count, images = inspection(catalog, ids, families, geometries, output, args.render)
    packed = []
    for image in bpy.data.images:
        if not image.packed_file:
            continue
        path = Path(bpy.path.abspath(image.filepath)).resolve()
        require(bytes(image.packed_file.data) == path.read_bytes(), 'Packed PBR source bytes changed')
        packed.append({'source': path.relative_to(catalog.root).as_posix(),
                       'sha256': sha(path), 'colorSpace': image.colorspace_settings.name})
        image.filepath = '//../../source/' + path.relative_to(catalog.root).as_posix()
    bpy.context.scene.render.filepath = '//previews/scout.png'
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'inspection.blend'), check_existing=False)
    write_json(output / 'scout-report.json', {'success': True, 'poseSamples': count, 'componentGroups': groups,
               'packedImages': packed, 'inspectionImages': images,
               'sourceSignature': catalog.signature(ids)[0], 'canonicalSourcesUnchanged': True})


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
