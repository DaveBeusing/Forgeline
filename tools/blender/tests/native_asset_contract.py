"""Native scene mutation checks; run through the pinned Blender launcher."""
import json
import sys
from pathlib import Path

import bpy

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, fixture_selection, read_geometry, require, write_glb, write_json
from roundtrip import export_scene, import_scene

request=json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig'))
project=Path(request['arguments'][0]);fixture=Path(request['arguments'][1]);output=Path(request['outputRoot'])
catalog=Catalog(request['inputRoot']);ids,families=fixture_selection(catalog,fixture,[])
geometries={a:read_geometry(catalog.source(a),catalog.root,catalog.definitions[a][1]) for a in ids if catalog.definitions[a][1]['type']=='mesh'}
results=[]

def pick(role):return next(o for o in bpy.context.scene.objects if o.get('fl_role')==role)

def verify(name, mutate, synthetic=False):
    if synthetic: import_scene(synthetic_catalog,synthetic_ids,synthetic_families,synthetic_geometry)
    else: bpy.ops.wm.open_mainfile(filepath=str(project))
    mutate()
    try:
        export_scene(synthetic_catalog,synthetic_ids,synthetic_families,synthetic_geometry) if synthetic else export_scene(catalog,ids,families,geometries)
    except ValueError as error:
        results.append({'name':name,'rejected':True,'error':str(error)})
    else:raise AssertionError(f'Invalid scene accepted: {name}')

verify('wrong scene units',lambda:setattr(bpy.context.scene.unit_settings,'scale_length',.01))
verify('wrong root axes',lambda:setattr(pick('root').rotation_euler,'x',.5))
verify('missing socket',lambda:bpy.data.objects.remove(pick('socket'),do_unlink=True))
verify('moved socket',lambda:setattr(pick('socket').location,'x',.1))
verify('rotated socket',lambda:setattr(pick('socket').rotation_euler,'z',.5))
verify('changed mechanical pivot',lambda:setattr(pick('part').location,'x',.1))
verify('missing state root',lambda:bpy.data.objects.remove(next(o for o in bpy.context.scene.objects if o.get('fl_role')=='root' and '.damaged' in o['fl_asset']),do_unlink=True))
verify('changed geometry',lambda:setattr(pick('geometry').data.vertices[0].co,'x',2))
verify('removed UV channel',lambda:pick('geometry').data.uv_layers.remove(pick('geometry').data.uv_layers[0]))
verify('changed logical name',lambda:setattr(pick('part'),'name','renamed part'))
verify('unsupported modifier',lambda:pick('geometry').modifiers.new('Unsupported bevel','BEVEL'))
verify('changed material nodes',lambda:next(m for m in bpy.data.materials if m.get('fl_asset')).node_tree.nodes.new('ShaderNodeTexNoise'))

def normal_mesh():return next(o for o in bpy.context.scene.objects if o.get('fl_role')=='geometry' and 'fl_normal' in o.data.attributes)
verify('removed authored normal',lambda:normal_mesh().data.attributes.remove(normal_mesh().data.attributes['fl_normal']))
def change_authored_normal():
    value=normal_mesh().data.attributes['fl_normal'].data[0]
    value.vector=(value.vector.x+.25,value.vector.y,value.vector.z)
verify('changed authored normal',change_authored_normal)
verify('changed split-normal preview',lambda:normal_mesh().data.normals_split_custom_set([(1,0,0)]*len(normal_mesh().data.loops)))

def edit_mode():
    obj=pick('geometry');bpy.context.view_layer.objects.active=obj;obj.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
verify('uncommitted edit mode',edit_mode)

# A separate two-slot fixture exercises authored tangents and slot ordering.
synthetic_root=output/'synthetic-source';synthetic_root.mkdir()
geometry={'document':{'asset':{'version':'2.0'},'nodes':[{'mesh':0}],'meshes':[{'name':'TwoSlotFixture','primitives':[{'material':0},{'material':1}]}]},'primitives':[]}
for slot in range(2):
    geometry['primitives'].append({'mesh':0,'primitive':slot,'name':'TwoSlotFixture','materialSlot':slot,'indices':[0,1,2],
        'attributes':{'POSITION':[(0,0,slot),(1,0,slot),(0,1,slot)],'NORMAL':[(0,0,1)]*3,'TEXCOORD_0':[(0,0),(1,0),(0,1)],'TANGENT':[(1,0,0,1)]*3}})
write_glb(synthetic_root/'mesh.glb',geometry)
write_json(synthetic_root/'mesh.asset.json',{'id':'mesh.synthetic','type':'mesh','source':'mesh.glb','materialReferences':['material.first','material.second']})
for name in ['first','second']:
    write_json(synthetic_root/(name+'.asset.json'),{'id':'material.'+name,'type':'material','source':name+'.material.json'})
    write_json(synthetic_root/(name+'.material.json'),{})
synthetic_fixture=output/'synthetic-fixture.json';write_json(synthetic_fixture,{'schemaVersion':1,'families':[{'root':'mesh.synthetic','assets':['mesh.synthetic'],'instanceScale':[2,3,4],'instanceTranslation':[1,2,3],'instanceYawDegrees':37}]})
synthetic_catalog=Catalog(synthetic_root);synthetic_ids,synthetic_families=fixture_selection(synthetic_catalog,synthetic_fixture,[])
synthetic_geometry={'mesh.synthetic':read_geometry(synthetic_root/'mesh.glb',synthetic_root,synthetic_catalog.definitions['mesh.synthetic'][1])}
import_scene(synthetic_catalog,synthetic_ids,synthetic_families,synthetic_geometry)
export_scene(synthetic_catalog,synthetic_ids,synthetic_families,synthetic_geometry)

def swap_slots():
    mesh=pick('geometry').data;materials=list(mesh.materials);mesh.materials.clear()
    for material in reversed(materials):mesh.materials.append(material)
verify('reordered material slots',swap_slots,True)
verify('wrong primitive material',lambda:setattr(pick('geometry').data.polygons[0],'material_index',1),True)
verify('changed tangent handedness',lambda:setattr(pick('geometry').data.attributes['fl_sign'].data[0],'value',-1),True)
write_json(output/'native-contracts.json',{'success':True,'nativeBlender':True,'cases':results,'syntheticTwoSlotTangentPositive':True})
print(f'[blender:contracts] {len(results)} invalid scenes rejected; two-slot/tangent positive passed')
