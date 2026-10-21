"""Copy the exact prop closure without scanning unrelated production domains."""
import json
import shutil
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, contained, fixture_selection, load_gltf, require, sha
output = Path(sys.argv[1]).resolve()
source = ROOT / 'assets/source'
require(output.is_relative_to(ROOT / 'artifacts') and not output.exists(), 'Use a fresh artifacts source directory')
fixture = Path(__file__).with_name('fixture.json')
expected = {aid for family in json.loads(fixture.read_text(encoding='utf-8'))['families'] for aid in family['assets']}
definitions = []
for directory in ('world/meshes/props', 'world/meshes/kit'):
    for path in (source / directory).glob('*.asset.json'):
        definition = json.loads(path.read_text(encoding='utf-8-sig'))
        if definition['id'].startswith('mesh.world.prop.'):
            definitions.append((path, definition))
require({d['id'] for _, d in definitions} == expected and len(definitions) == 44, 'Canonical prop roster changed')
for relative in ('world/meshes/kit/collision_centered.asset.json',
    'world/materials/common/industrial.asset.json',
    'materials/directorate/surfaces/surface_atlas.asset.json',
    'materials/directorate/surfaces/surface_normal.asset.json',
    'materials/directorate/surfaces/surface_orm.asset.json'):
    path = source / relative
    definitions.append((path, json.loads(path.read_text(encoding='utf-8-sig'))))
files = set()
for path, definition in definitions:
    actual = contained(source, path.parent / definition['source'])
    files.update((path, actual))
    if definition['type'] == 'mesh':
        files.update(load_gltf(actual, source)[2])
for path in files:
    target = output / path.relative_to(source)
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(path, target)
    require(sha(path) == sha(target), 'Scoped source bytes changed')
clone = Catalog(output)
ids, families = fixture_selection(clone, fixture, [])
require(len(ids) == 49, 'Shared prop dependency closure changed')
require(set(clone.files(ids)) == {output / path.relative_to(source) for path in files}, 'Unresolved snapshot dependency')
print(f'Copied {len(families)} prop families / {len(ids)} assets with exact raw source hashes')
