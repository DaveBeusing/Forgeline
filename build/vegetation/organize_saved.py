"""Add anatomic selection groups to verified rest projects, then reopen and validate."""
import json
import sys
from pathlib import Path
import bpy
ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT/'tools/blender'), str(Path(__file__).resolve().parent)]
from asset_contract import Catalog, fixture_selection, read_geometry, sha, write_json
from roundtrip import export_scene
from anatomy import add_groups
request = json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig'))
output = Path(request['outputRoot'])
source = (ROOT / Path(request['arguments'][0])).resolve()
catalog = Catalog(source/'candidate-source')
fixture = Path(__file__).with_name('fixture.json')
_, families = fixture_selection(catalog, fixture, [])
rows = []
for family in families:
    name = family['root'].split('.')[-1]
    ids, selected = fixture_selection(catalog, fixture, [family['root']])
    geometries = {aid: read_geometry(catalog.source(aid),catalog.root,catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type']=='mesh'}
    bpy.ops.wm.open_mainfile(filepath=str(source/name/'first/authoring.blend'))
    groups = add_groups()
    export_scene(catalog,ids,selected,geometries)
    target = source/name/'editable.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(target),check_existing=False)
    bpy.ops.wm.open_mainfile(filepath=str(target))
    export_scene(catalog,ids,selected,geometries)
    rows.append({'root':family['root'],'masterSha256':sha(target),'groups':groups,'reopened':True})
write_json(output/'editorial-masters.json',{'schemaVersion':1,'success':True,'families':rows})
