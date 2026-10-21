"""Native rooted vegetation authoring through the bounded static asset adapter."""
import copy
import json
import runpy
import sys
from pathlib import Path
import bpy

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog, compare_geometry, fixture_selection, read_geometry, require, sha, write_glb, write_json
import roundtrip
import production
from geometry import refine
from anatomy import add_groups
FIXTURE = Path(__file__).with_name('fixture.json')
NAMES = ('conifer', 'broadleaf', 'scrub', 'grass_clump')


def prepare(source, output):
    catalog = Catalog(source)
    ids, families = fixture_selection(catalog, FIXTURE, [])
    target = output / 'candidate-source'
    catalog.copy_to(ids, target)
    changes = []
    for reduced, sides in ((False, 10), (True, 6)):
        aid = 'mesh.world.vegetation.conifer' + ('_lod1' if reduced else '')
        path, definition = catalog.definitions[aid]
        geometry = read_geometry(catalog.source(aid), catalog.root, definition)
        require(len(geometry['primitives']) == 1, 'Conifer topology changed')
        p = geometry['primitives'][0]
        require(len(p['indices']) == sides * 24, 'Conifer budget changed')
        positions = p['attributes']['POSITION']
        stem = sorted(set(p['indices'][:sides * 6]))
        require(len(stem) == sides * 6 and all(0 <= positions[v][1] <= .650001 and abs(positions[v][0]) <= .055001 and abs(positions[v][2]) <= .055001 for v in stem), 'Unrecognized stem partition')
        uv = p['attributes']['TEXCOORD_0']
        lo, hi = [min(x[i] for x in uv) for i in range(2)], [max(x[i] for x in uv) for i in range(2)]
        require(all(.017 < u < .233 and .767 < v < .983 for u,v in uv), 'Conifer foliage atlas cell changed')
        for v in stem:
            uv[v] = tuple(.518 + (uv[v][i]-lo[i])/(hi[i]-lo[i])*.214 for i in range(2))
        relative = path.relative_to(catalog.root)
        filename = Path('_vegetation') / (aid + '.glb')
        write_glb(target / relative.parent / filename, geometry)
        updated = copy.deepcopy(definition)
        updated['source'] = filename.as_posix()
        write_json(target / relative, updated)
        changes.append({'id': aid, 'stemVertices': len(stem), 'fromCell': 12, 'toCell': 10, 'triangles': sides * 8, 'positionsNormalsAndIndicesUnchanged': True})
    for name in ('scrub', 'grass_clump'):
        for reduced in (False, True):
            aid = 'mesh.world.vegetation.' + name + ('_lod1' if reduced else '')
            path, definition = catalog.definitions[aid]
            before = read_geometry(catalog.source(aid), catalog.root, definition)
            geometry = refine(name, before, reduced)
            relative = path.relative_to(catalog.root)
            filename = Path('_vegetation') / (aid + '.glb')
            write_glb(target / relative.parent / filename, geometry)
            updated = copy.deepcopy(definition)
            updated['source'] = filename.as_posix()
            write_json(target / relative, updated)
            changes.append({'id': aid, 'geometryRefined': True, 'triangles': len(geometry['primitives'][0]['indices'])//3, 'originalBoundsAndBudgetsPreserved': True})
    candidate = Catalog(target)
    candidate.validate(ids)
    write_json(output / 'candidate-report.json', {'schemaVersion': 1, 'canonicalSignature': catalog.signature(ids)[0], 'candidateSignature': candidate.signature(ids)[0], 'changes': changes, 'newTextures': 0, 'newMaterials': 0, 'productionPromotion': 'blocked-pending-render-qualification'})
    return candidate, ids, families


def organize(root):
    add_groups()
    for aid in sorted({o.get('fl_asset') for o in bpy.context.scene.objects}):
        collection = bpy.data.collections.new(aid)
        bpy.context.scene.collection.children.link(collection)
        for obj in list(bpy.context.scene.objects):
            if obj.get('fl_asset') != aid:
                continue
            for old in list(obj.users_collection):
                old.objects.unlink(obj)
            collection.objects.link(obj)
            obj.hide_set(aid != root)
            obj.hide_render = aid != root
    bpy.context.scene['fl_rooted_wind'] = 'Runtime only: eight seconds, maximum .025 radians, fade 18 to 72 reference pixels. No authored animation.'


def export(request, catalog, root, output, blend=None):
    args = ['--fixtures', str(FIXTURE), '--asset-id', root]
    if blend:
        args += ['--action', 'Export', '--blend-file', str(blend)]
    roundtrip.run(dict(request, inputRoot=str(catalog.root), outputRoot=str(output), arguments=args))
    if not blend:
        organize(root)
        bpy.ops.wm.save_as_mainfile(filepath=str(output / 'authoring.blend'), check_existing=False)
    report = json.loads((output / 'roundtrip-report.json').read_text())
    exported = Catalog(output / 'source')
    for row in report['meshes']:
        aid = row['id']
        baseline = read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
        actual = read_geometry(exported.source(aid), exported.root, catalog.definitions[aid][1])
        deviations = compare_geometry(baseline, actual)
        require(max(deviations.values()) <= 5e-8, 'Native storage changed artwork beyond float noise')
        row['nativeReadbackDeviations'] = deviations
        row['nativeReadbackSha256'] = sha(exported.source(aid))
        write_glb(exported.source(aid), baseline)
        row['exportSha256'] = sha(exported.source(aid))
    report['candidateSourceFloatsExact'] = True
    write_json(output / 'roundtrip-report.json', report)


def run(request):
    output = Path(request['outputRoot'])
    if '--resume' in request['arguments']:
        catalog = Catalog(output / 'candidate-source')
        ids, families = fixture_selection(catalog, FIXTURE, [])
        prior = json.loads((output / 'candidate-report.json').read_text())
        require(prior['candidateSignature'] == catalog.signature(ids)[0] and prior['canonicalSignature'] == Catalog(request['inputRoot']).signature(ids)[0], 'Resume inputs changed')
    else:
        catalog, ids, families = prepare(request['inputRoot'], output)
    for name in NAMES:
        root = 'mesh.world.vegetation.' + name
        family = output / name
        for variant in ('first', 'second', 'reopened'):
            target = family / variant
            if '--resume' in request['arguments'] and (target / 'roundtrip-report.json').is_file():
                prior = json.loads((target / 'roundtrip-report.json').read_text())
                require(prior['success'] and prior['candidateSourceFloatsExact'], 'Incomplete resumed native output')
                retained = Catalog(target / 'source')
                require(all(row['exportSha256'] == sha(retained.source(row['id'])) for row in prior['meshes']), 'Resumed export bytes changed')
                continue
            target.mkdir(parents=True, exist_ok=False)
            export(request, catalog, root, target, family / 'first/authoring.blend' if variant == 'reopened' else None)
        local_fixture = family / 'fixture.json'
        write_json(local_fixture, {'schemaVersion': 1, 'families': [f for f in families if f['root'] == root]})
        edit = family / 'edit-check'
        if not (edit / 'master-report.json').is_file():
            edit.mkdir(exist_ok=True)
            module = runpy.run_path(str(Path(__file__).with_name('edit_contract.py')))
            module['run'](dict(request, inputRoot=str(catalog.root), outputRoot=str(edit), arguments=[str(family / 'first/authoring.blend'), str(local_fixture)]))
        rejection = family / 'native-contracts'
        rejection.mkdir(exist_ok=True)
        reject_request = rejection / 'request.json'
        write_json(reject_request, dict(request, inputRoot=str(catalog.root), outputRoot=str(rejection), arguments=[str(family / 'first/authoring.blend'), str(FIXTURE), root]))
        sys.argv = [str(Path(__file__).with_name('native_contract.py')), '--request', str(reject_request)]
        runpy.run_path(sys.argv[0], run_name='__main__')
    geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
    roundtrip.import_scene(catalog, ids, families, geometries)
    costs = production.surface_preview(catalog, ids)
    previews = production.previews(families, geometries, output, '--render' in request['arguments'])
    packed = []
    for image in bpy.data.images:
        if image.packed_file:
            source = Path(bpy.path.abspath(image.filepath))
            require(bytes(image.packed_file.data) == source.read_bytes(), 'Packed source changed')
            packed.append({'source': source.relative_to(catalog.root).as_posix(), 'sha256': sha(source), 'colorSpace': image.colorspace_settings.name})
            image.filepath = '//textures/' + source.name
    bpy.context.scene.render.filepath = '//previews/vegetation.png'
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'vegetation_inspection.blend'), check_existing=False)
    write_json(output / 'native-batch.json', {'schemaVersion': 1, 'success': True, 'blenderVersion': bpy.app.version_string, 'families': NAMES, 'scripts': {p.name: sha(p) for p in Path(__file__).parent.glob('*.py')}, 'packedImages': packed, 'textures': costs, 'previews': previews, 'canonicalSourcesUnchanged': Catalog(request['inputRoot']).signature(ids)[0] == json.loads((output / 'candidate-report.json').read_text())['canonicalSignature']})


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
