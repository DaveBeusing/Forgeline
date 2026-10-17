"""Real bpy modifier and material preview regression cases."""
import copy
import json
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import bpy
from asset_contract import write_json, require
from production import validate_job, prepare_geometry
from production_contract import gate, retarget_geometry
from roundtrip import import_scene

request=json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig'))
# Connected planar tessellation: dissolve removes interior edges without changing its role form or UV chart.
positions=[(x/2,y/2,0) for y in range(3) for x in range(3)]
indices=[]
for y in range(2):
    for x in range(2):
        a=y*3+x;indices.extend((a,a+1,a+4,a,a+4,a+3))
g={'document':{'asset':{'version':'2.0'},'nodes':[{'mesh':0}],'meshes':[{'name':'Chart','primitives':[{}]}]},'primitives':[{'mesh':0,'primitive':0,'name':'Chart','materialSlot':-1,'indices':indices,'attributes':{'POSITION':positions,'NORMAL':[(0,0,1)]*9,'TEXCOORD_0':[(p[0],p[1]) for p in positions]}}]}
d={'id':'mesh.surface_fixture','type':'mesh','source':'fixture.glb'}
class FixtureCatalog:
    definitions={'mesh.surface_fixture':(None,d)}
    def signature(self,ids):return ('fixture',{})
limits={'maxTriangles':8,'maxSections':1,'maxGeometryBytes':528,'minSilhouetteIou':.99,'maxRelativeBoundsError':1e-6}
passed=[]
def reset(definition=d):
    catalog=FixtureCatalog();catalog.definitions={'mesh.surface_fixture':(None,definition)}
    import_scene(catalog,[],[],{'mesh.surface_fixture':g})
def reject(name,action):
    try:action()
    except ValueError:passed.append(name);return
    raise AssertionError('Invalid production operation accepted: '+name)
for name,job in [('ratio',{'ratio':0}),('bevel',{'bevelMetres':.1}),('method',{'method':'UNSUBDIV'}),('nonfinite',{'ratio':float('nan')}),('unknown option',{'bake':True})]:
    reject(name,lambda job=job:validate_job(job))
reset();candidate,_=prepare_geometry(d['id'],d,g,{'method':'DISSOLVE','weightedNormals':True})
measurement=gate(g,candidate,limits)
require(measurement['candidate']['triangles']<8,'Real planar reduction did not reduce triangles');passed.append('native planar reduction + weighted normal + tangent export')
target=copy.deepcopy(g);target['primitives'][0]['name']='Chart_LOD2';target['document']['meshes'][0]['name']='Chart_LOD2'
lod=retarget_geometry(target,candidate);gate(target,lod,limits)
require(lod['primitives'][0]['name']=='Chart_LOD2','Generated LOD lost stable mesh identity');passed.append('generated LOD keeps target identity and budget')
reset();candidate,_=prepare_geometry(d['id'],d,g,{'ratio':.75})
gate(g,candidate,{**limits,'minSilhouetteIou':0,'maxRelativeBoundsError':1});passed.append('native collapse decimation')
reset();candidate,_=prepare_geometry(d['id'],d,g,{'bevelMetres':.001,'bevelSegments':2})
gate(g,candidate,limits);passed.append('calibrated bevel stack and UV preservation')
reset();reject('missing protected part',lambda:prepare_geometry(d['id'],d,g,{'protectedParts':['missing']}))
# A socket-bound moving partition cannot be decimated.
protected={**d,'sockets':[{'name':'pivot','x':0,'y':0,'z':0}],'mechanicalParts':[{'name':'moving','firstIndex':0,'indexCount':len(indices),'socket':'pivot','parent':-1}]}
reset(protected);candidate,updated=prepare_geometry(d['id'],protected,g,{'ratio':.1})
require(len(candidate['primitives'][0]['indices'])==len(indices),'Moving partition was reduced')
require(updated['sockets']==protected['sockets'] and updated['mechanicalParts']==protected['mechanicalParts'],'Moving pivot or range changed');passed.append('moving pivot and partition protection')
write_json(Path(request['outputRoot'])/'native-production-contracts.json',{'success':True,'passed':passed,'planarReduction':measurement})
print('Native production contracts passed:',len(passed))
