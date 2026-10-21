"""Real saved-project rejection and edit-survival proofs for each logistics family."""
import json
import sys
from pathlib import Path
import bpy
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, fixture_selection, require, sha, write_json
from logistics import run

request = json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig'))
family, master = request['arguments']
master = Path(master)
output = Path(request['outputRoot'])
source = Path(request['inputRoot'])
catalog = Catalog(source)
fixture = Path(__file__).resolve().parents[1] / 'fixtures' / ('logistics_' + family + '.json')
ids, _ = fixture_selection(catalog, fixture, [])
signature = catalog.signature(ids)[0]
master_hash = sha(master)


def export(project, target):
    run({'inputRoot': str(source), 'outputRoot': str(target), 'arguments':
         ['--action', 'Export', '--family', family, '--blend-file', str(project)]})


export(master, output / 'control')
results = []


def pick(role):
    return next(o for o in bpy.context.scene.objects if o.get('fl_role') == role)


def reject(name, mutate):
    bpy.ops.wm.open_mainfile(filepath=str(master))
    mutate()
    project = output / (str(len(results)) + '.blend')
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(project), check_existing=False)
    try:
        export(project, output / ('reject-' + str(len(results))))
    except ValueError as error:
        results.append({'name': name, 'rejected': True, 'error': str(error)})
    else:
        raise AssertionError('Invalid logistics edit accepted: ' + name)


reject('metre scale', lambda: setattr(bpy.context.scene.unit_settings, 'scale_length', .01))
reject('rotated root', lambda: setattr(pick('root').rotation_euler, 'x', .5))
reject('moved service socket', lambda: setattr(pick('socket').location, 'x', .4))
reject('detached module', lambda: setattr(pick('part').location, 'y', .1))
reject('outside retained bounds', lambda: setattr(pick('geometry').data.vertices[0].co, 'x', 4))
reject('removed UV', lambda: pick('geometry').data.uv_layers.remove(pick('geometry').data.uv_layers[0]))
reject('unsupported modifier', lambda: pick('geometry').modifiers.new('Subdivision', 'SUBSURF'))
reject('added animation', lambda: pick('root').keyframe_insert(data_path='location', frame=1))
reject('untracked object', lambda: bpy.context.scene.collection.objects.link(bpy.data.objects.new('Extra', None)))
reject('changed PBR graph', lambda: next(m for m in bpy.data.materials if m.get('fl_asset')).node_tree.nodes.new('ShaderNodeTexNoise'))
reject('changed split normals', lambda: pick('geometry').data.normals_split_custom_set([(1,0,0)]*len(pick('geometry').data.loops)))

bpy.ops.wm.open_mainfile(filepath=str(master))
root = 'building.directorate.' + family
kit = json.loads((source / 'buildings/directorate/authoring' / (family + '.kit.json')).read_text())
# Translate a complete interior module, keeping every triangle plane and socket.
obj = next(o for o in bpy.context.scene.objects if o.get('fl_role') == 'geometry'
           and o.get('fl_asset') == root and
           min(v.co.x for v in o.data.vertices) > kit['bounds']['min'][0]+.01 and
           max(v.co.x for v in o.data.vertices) < kit['bounds']['max'][0]-.01)
for vertex in obj.data.vertices: vertex.co.x += .002
positive = output / 'positive.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(positive), check_existing=False)
export(positive, output / 'positive')
control = Catalog(output / 'control/source')
edited = Catalog(output / 'positive/source')
changed = [aid for aid in ids if sha(control.source(aid)) != sha(edited.source(aid))]
require(changed == [root], 'Saved module edit did not survive exclusively in its own LOD0')
require(sha(master) == master_hash and catalog.signature(ids)[0] == signature, 'Production sources changed')
write_json(output / 'contracts.json', {'success': True, 'family': family, 'rejections': results,
           'savedEditChangedIds': changed, 'masterSha256': master_hash, 'productionUnchanged': True})
