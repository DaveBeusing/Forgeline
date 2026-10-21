"""Preserve the integrated artillery kit using the existing strict Blender adapter."""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, fixture_selection, read_geometry, require, sha, write_json
import roundtrip
import mechanical


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
                        'Artillery inspection exceeds the existing runtime matrix tolerance')
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
    # The rest project remains a strict preservation input. Generated actions and
    # packed PBR graphs belong to the separate inspection project only.
    if args.action != 'Export':
        mechanical.add_properties(catalog)
    updates = mechanical.author_definitions(catalog, geometries, {'maxPivotDeltaMetres': 0.025})
    require(all(value == catalog.definitions[aid][1] for aid, value in updates.items()),
            'Preservation master cannot change mechanical metadata; use the established motion authoring path')
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
    bpy.context.scene.render.filepath = '//previews/artillery.png'
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'inspection.blend'), check_existing=False)
    write_json(output / 'artillery-report.json', {'success': True, 'poseSamples': count,
               'packedImages': packed, 'inspectionImages': images,
               'sourceSignature': catalog.signature(ids)[0], 'canonicalSourcesUnchanged': True})


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
