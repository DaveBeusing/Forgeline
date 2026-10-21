"""Shorten only byte-verified exports created by the qualified prop binding step."""
import json
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog, require, sha, write_json
from retained import FAMILIES, ORIGINAL, qualified
original, axis = Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve()
qualified(original); qualified(axis)
source = ROOT / 'assets/source'
owned = (source / 'world/meshes/sources/blender/props').resolve()
records = {entry['definition']['id']: entry for entry in json.loads(ORIGINAL.read_text(encoding='utf-8'))['bindings']}
pending = []
for family in FAMILIES:
    name = family['root'].removeprefix('mesh.world.prop.')
    exports = Catalog((axis if name == 'pipe_section' else original / name) / 'first/source')
    for aid in family['assets']:
        entry = records[aid]
        path = source / entry['path']
        current = json.loads(path.read_text(encoding='utf-8-sig'))
        expected = dict(entry['definition']); expected.pop('source')
        metadata = dict(current); old_binding = metadata.pop('source')
        require(metadata == expected, 'Unrelated metadata changed')
        require(old_binding == '../sources/blender/props/' + name + '/' + aid + '.glb', 'Unexpected current binding')
        old = (path.parent / old_binding).resolve()
        leaf = 'reduced.glb' if aid.endswith('_lod1') else 'high.glb'
        target = old.with_name(leaf)
        require(old.is_relative_to(owned) and target.is_relative_to(owned) and old.parent == target.parent, 'Export path escaped owned source directory')
        require(not target.exists() and sha(old) == sha(exports.source(aid)), 'Existing export differs from qualified bytes')
        updated = dict(current, source='../sources/blender/props/' + name + '/' + leaf)
        pending.append((old, target, path, updated, sha(old)))
for old, target, path, updated, expected_sha in pending:
    old.rename(target)
    require(sha(target) == expected_sha, 'Renamed export bytes changed')
    write_json(path, updated)
print('Shortened 44 verified export paths; every GLB byte, stable ID and other definition field is unchanged')
