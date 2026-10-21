"""Actual saved-master positive and rejection checks for the Command Core."""
import json
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, fixture_selection, require, sha, write_json
from command_core import run
from command_core_contract import ROOT, SUFFIXES

request = json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig'))
output = Path(request['outputRoot'])
master, fixture = map(Path, request['arguments'])
source = Path(request['inputRoot'])
master_hash = sha(master)
production = Catalog(source)
ids, _ = fixture_selection(production, fixture, [])
source_signature = production.signature(ids)[0]
production.copy_to(ids, output / 'input')
source = output / 'input'


def export(project, target):
    run({'inputRoot': str(source), 'outputRoot': str(target), 'arguments':
        ['--action','Export','--fixtures',str(fixture),'--blend-file',str(project)]})


export(master, output / 'control')
results = []


def pick(role):
    return next(obj for obj in bpy.context.scene.objects if obj.get('fl_role') == role)


def reject(name, mutate):
    bpy.ops.wm.open_mainfile(filepath=str(master))
    mutate()
    project = output / (str(len(results)) + '.blend')
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(project), check_existing=False)
    try:
        export(project, output / ('rejected-' + str(len(results))))
    except ValueError as error:
        results.append({'name': name, 'rejected': True, 'error': str(error)})
    else:
        raise AssertionError('Invalid Command Core edit accepted: ' + name)


reject('wrong metre scale', lambda: setattr(bpy.context.scene.unit_settings, 'scale_length', .01))
reject('rotated root', lambda: setattr(pick('root').rotation_euler, 'x', .5))
reject('moved socket', lambda: setattr(pick('socket').location, 'x', .4))
reject('detached module frame', lambda: setattr(pick('part').location, 'y', .1))
reject('outside silhouette', lambda: setattr(pick('geometry').data.vertices[0].co, 'x', 4))
reject('removed UV', lambda: pick('geometry').data.uv_layers.remove(pick('geometry').data.uv_layers[0]))
reject('unapplied modifier', lambda: pick('geometry').modifiers.new('Unsupported subdivision', 'SUBSURF'))
reject('added animation', lambda: pick('root').keyframe_insert(data_path='location', frame=1))
reject('untracked object', lambda: bpy.context.scene.collection.objects.link(bpy.data.objects.new('Extra geometry', None)))
reject('changed preview graph', lambda: next(m for m in bpy.data.materials if m.get('fl_asset')).node_tree.nodes.new('ShaderNodeTexNoise'))
reject('missing construction root', lambda: bpy.data.objects.remove(next(o for o in bpy.context.scene.objects
    if o.get('fl_role') == 'root' and o.get('fl_asset') == ROOT + '.foundation'), do_unlink=True))
reject('changed split normals', lambda: pick('geometry').data.normals_split_custom_set([(1,0,0)] * len(pick('geometry').data.loops)))

bpy.ops.wm.open_mainfile(filepath=str(master))
obj = next(obj for obj in bpy.context.scene.objects if obj.get('fl_role') == 'geometry'
           and obj.get('fl_asset') == ROOT and obj.parent.get('fl_logical_name') == 'service_piping')
for vertex in obj.data.vertices:
    vertex.co.y -= .002
positive = output / 'positive.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(positive), check_existing=False)
export(positive, output / 'positive')
control = Catalog(output / 'control/source')
changed = Catalog(output / 'positive/source')
require(sha(control.source(ROOT)) != sha(changed.source(ROOT)), 'Positive saved edit did not change the primary export')
for suffix in SUFFIXES[1:]:
    require(sha(control.source(ROOT + suffix)) == sha(changed.source(ROOT + suffix)), 'Positive edit leaked into another state/LOD')
require(sha(master) == master_hash, 'Production master was overwritten')
require(production.signature(ids)[0] == source_signature, 'Production sources changed')
write_json(output / 'native-contracts.json', {'success': True, 'nativeBlender': True, 'cases': results,
    'positiveSavedEditChangesOnlyPrimary': True, 'productionMasterUnchanged': True})
