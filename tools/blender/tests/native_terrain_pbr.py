"""Real saved-source rejection controls and an actual bounded material edit."""
import argparse
import json
from pathlib import Path
import sys
import bpy
import numpy as np

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from terrain_pbr import validate, export
from terrain_pbr_contract import FAMILIES, digest, require
from procedural_pbr import rgba

p=argparse.ArgumentParser();p.add_argument('--request',required=True)
request=json.loads(Path(p.parse_args(sys.argv[sys.argv.index('--')+1:]).request).read_text(encoding='utf-8-sig'))
source=Path(request['inputRoot']);out=Path(request['outputRoot'])
results=[]
for family in FAMILIES:
    master=source/'world/authoring/terrain'/(family+'.blend');before=digest(master)
    bpy.ops.wm.open_mainfile(filepath=str(master),load_ui=False)
    record,obj,mat=validate(source,family);n=mat.node_tree.nodes
    cases=[('tint',n['Tint'].outputs[0],'default_value',(1.21,1,1,1)),
           ('roughness',n['RoughnessBias'].outputs[0],'default_value',.06),
           ('detail',n['DetailAmplitude'].outputs[0],'default_value',.05),
           ('linear normal',n['Source:normal'].image.colorspace_settings,'name','sRGB'),
           ('linear ORM',n['Source:orm'].image.colorspace_settings,'name','sRGB'),
           ('normal filtering',n['Source:normal'],'interpolation','Linear'),
           ('noise recipe',n['PeriodicNoise'].inputs['Scale'],'default_value',9),
           ('ORM semantics',n['ORMChannels'],'mode','HSV'),
           ('metre units',bpy.context.scene.unit_settings,'scale_length',.01),
           ('UV orientation',obj.data.uv_layers.active.data[0],'uv',(.1,.1)),
           ('swatch transform',obj,'location',(1,0,0))]
    rejected=[]
    for label,target,key,value in cases:
        original=getattr(target,key)
        if not isinstance(original,(str,int,float,bool)):original=tuple(original)
        setattr(target,key,value)
        try:
            try:validate(source,family)
            except (ValueError,KeyError):rejected.append(label)
            else:raise AssertionError('Accepted '+label)
        finally:setattr(target,key,original)
        validate(source,family)
    extra=n.new('ShaderNodeValue')
    try:
        try:validate(source,family)
        except ValueError:rejected.append('unsupported graph')
        else:raise AssertionError('Accepted extra graph node')
    finally:n.remove(extra)
    validate(source,family)
    require(digest(master)==before,'Native controls modified the saved master')
    results.append({'family':family,'masterSha256':before,'rejections':rejected})
    (out/'controls.json').write_text(json.dumps(results,indent=2)+'\n',encoding='utf-8')

master=source/'world/authoring/terrain/dry_dirt.blend'
bpy.ops.wm.open_mainfile(filepath=str(master),load_ui=False)
mat=bpy.data.materials['Terrain:dry_dirt'];mat.node_tree.nodes['Tint'].outputs[0].default_value=(.96,1,1,1)
edited=out/'edited.blend';bpy.ops.wm.save_as_mainfile(filepath=str(edited),compress=True)
report=export(source,'dry_dirt',edited,out/'positive')
require(report['controls']['tint'][0]<.97,'Saved edit did not survive reopening')
for kind in ('base','normal','orm'):
    original=rgba(source/'world/textures/terrain'/f'dry_dirt_production_{kind}.png')
    baked=rgba(out/'positive/source'/report['maps'][kind]['source'])
    equal=np.array_equal(original,baked)
    require(not equal if kind=='base' else equal, 'Saved tint must change only the Base Color pixels')
report['basePixelsChanged']=True
report['normalAndOrmPixelsUnchanged']=True
(out/'qualification.json').write_text(json.dumps({'success':True,'families':results,'savedTintEdit':report},indent=2)+'\n',encoding='utf-8')
