"""Bake the existing pipe's axis correction through actual saved Blender meshes."""
import copy
import json
import sys
from pathlib import Path
import bpy
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from prop_contract import PropCatalog
from pipe_contract import align_straight_pipe
from asset_contract import Catalog, compare_geometry, fixture_selection, geometry_stats, read_geometry, require, sha, write_glb, write_json
import roundtrip
request = json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig'))
output = Path(request['outputRoot'])
catalog = PropCatalog(request['inputRoot'])
ids, families = fixture_selection(catalog, ROOT / 'build/world-props/fixture.json', ['mesh.world.prop.pipe_section'])
original_bindings = {x['definition']['id']: x['definition'] for x in json.loads((ROOT / 'build/world-props/original-bindings.json').read_text(encoding='utf-8'))['bindings']}
for aid in families[0]['assets']:
    require(catalog.definitions[aid][1] == original_bindings[aid], 'Axis correction requires the original audited glTF bindings')
signature, raw = catalog.signature(ids)
original = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
    for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
require(set(original) == {'mesh.world.prop.pipe_section', 'mesh.world.prop.pipe_section_lod1'}, 'Unexpected pipe dependency')
approved = {aid: align_straight_pipe(g) for aid, g in original.items()}
for aid in original:
    before, after = geometry_stats(original[aid]), geometry_stats(approved[aid])
    require(before == after, 'Pipe correction changed envelope, topology or vertex count')
records = []
for variant in ('first', 'second', 'reopened'):
    target = output / variant; target.mkdir(parents=True, exist_ok=False)
    if variant == 'reopened':
        bpy.ops.wm.open_mainfile(filepath=str(output / 'first/authoring.blend'))
    else:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        roundtrip.import_scene(catalog, ids, families, approved)
    results, meshes, deviations = roundtrip.export_scene(catalog, ids, families, approved)
    if variant != 'reopened':
        bpy.ops.wm.save_as_mainfile(filepath=str(target / 'authoring.blend'), check_existing=False)
    catalog.copy_to(ids, target / 'baseline-source')
    catalog.copy_to(ids, target / 'source')
    exports = {}
    for aid, geometry in results.items():
        path, definition = catalog.definitions[aid]
        relative = path.relative_to(catalog.root)
        source = Path('_pipe_axis') / (aid + '.glb')
        destination = target / 'source' / relative.parent / source
        write_glb(destination, geometry)
        compare_geometry(approved[aid], read_geometry(destination, target / 'source', definition))
        updated = copy.deepcopy(definition); updated['source'] = source.as_posix()
        write_json(target / 'source' / relative, updated)
        exports[aid] = sha(destination)
    if records:
        require(records[0]['exports'] == exports, 'Native pipe exports are not deterministic')
    records.append({'variant': variant, 'exports': exports, 'meshes': meshes, 'deviations': deviations})
require(catalog.signature(ids) == (signature, raw), 'Pipe correction changed original source bytes')
write_json(output / 'native-axis.json', {'schemaVersion': 1, 'success': True,
    'sourceSignature': signature, 'sourceHashes': raw, 'gameRotation': 'positive-Y-90-degrees',
    'boundsAndTopologyUnchanged': True, 'blenderVersion': bpy.app.version_string,
    'original': {aid: geometry_stats(g) for aid, g in original.items()}, 'variants': records,
    'canonicalSourcesUnchanged': True, 'humanArtApproval': 'pending'})
