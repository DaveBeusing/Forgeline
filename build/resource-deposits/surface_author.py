"""Original periodic mineral PBR authoring; no compiler, test or GPU capture invocation.

Blender 5.2.2: --python surface_author.py -- --action Author
Rebake edited recipes into a fresh --output-root under artifacts, or use Export
to extract packed, saved image edits from the four native material projects.
"""
import argparse
import hashlib
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'assets/source'
AUTHOR=SOURCE/'world/authoring/resource_surfaces'
sys.path.insert(0,str(ROOT/'tools/blender'))
from asset_contract import write_json, sha
from pbr_png import write_png, inspect_png

MAPS=('base','normal','orm')
PRESETS=((.80,.55,.11),(.15,1.0,.10),(1.0,.35,.09),(.40,.70,.23))


def smooth(a,b,x):
    t=np.clip((x-a)/(b-a),0,1)
    return t*t*(3-2*t)


def hashed(x,y,seed):
    value=(np.asarray(x,dtype=np.uint64)*374761393+np.asarray(y,dtype=np.uint64)*668265263+seed*1442695041)&0xffffffff
    value=((value^(value>>13))*1274126177)&0xffffffff
    return ((value^(value>>16))&0xffffffff).astype(np.float64)/4294967295


def noise(n,frequency,seed):
    p=(np.arange(n,dtype=np.float64)+.5)*frequency/n
    index=np.floor(p).astype(int);blend=p-index;blend=blend**3*(blend*(blend*6-15)+10)
    x=index[None,:];y=index[:,None];u=blend[None,:];v=blend[:,None]
    a=hashed(x%frequency,y%frequency,seed);b=hashed((x+1)%frequency,y%frequency,seed)
    c=hashed(x%frequency,(y+1)%frequency,seed);d=hashed((x+1)%frequency,(y+1)%frequency,seed)
    return (a*(1-u)+b*u)*(1-v)+(c*(1-u)+d*u)*v


def cells(n,frequency,seed):
    p=(np.arange(n,dtype=np.float64)+.5)*frequency/n
    ix=np.floor(p).astype(int)[None,:];iy=np.floor(p).astype(int)[:,None]
    x=p[None,:];y=p[:,None];first=np.full((n,n),10.);second=first.copy()
    for dy in (-1,0,1):
        for dx in (-1,0,1):
            cx=ix+dx;cy=iy+dy
            fx=cx+.16+.68*hashed(cx%frequency,cy%frequency,seed)
            fy=cy+.16+.68*hashed(cx%frequency,cy%frequency,seed+17)
            distance=np.sqrt((x-fx)**2+(y-fy)**2)
            second=np.minimum(second,np.maximum(first,distance));first=np.minimum(first,distance)
    return first,second-first


def srgb(linear):
    return np.where(linear<=.0031308,12.92*linear,1.055*np.maximum(linear,0)**(1/2.4)-.055)


def rgba(channels):
    if channels.ndim==2:channels=np.repeat(channels[:,:,None],3,axis=2)
    result=np.full((*channels.shape[:2],4),255,dtype=np.uint8)
    result[:,:,:3]=np.rint(np.clip(channels,0,1)*255).astype(np.uint8)
    return result


def tile(recipe,n,variant,gutter):
    seed=recipe['seed']+variant*337
    macro=noise(n,3,seed);meso=noise(n,17,seed+3);micro=noise(n,97,seed+7)
    dust=noise(n,41,seed+11);nearest,gap=cells(n,5,seed+29)
    u,v=np.meshgrid((np.arange(n)+.5)/n,(np.arange(n)+.5)/n)
    weathering,fracture_gain,inclusion_radius=PRESETS[variant]
    pattern=recipe['pattern']
    if pattern=='stratified_veins':
        strata=np.abs(np.sin(math.tau*(3*u+v+.22*macro+.05*meso)))
        fractures=1-smooth(.025,.16,strata)
        relief=.45*macro+.20*meso+.12*strata
    elif pattern=='cleaved_facets':
        planes=np.minimum(np.abs(np.sin(math.tau*(4*u+v+.05*macro))),np.abs(np.sin(math.tau*(-u+3*v))))
        fractures=np.maximum(1-smooth(.025,.13,planes),1-smooth(.025,.12,gap))
        relief=.22*macro+.18*meso+.10*np.floor(nearest*5)/5
    elif pattern=='sealed_nodules':
        bands=.5+.5*np.sin(math.tau*(nearest*3+.15*macro))
        fractures=(1-smooth(.015,.10,gap))*.7
        relief=.16*macro+.12*meso+.27*(1-smooth(.1,.75,nearest))+.04*bands
    elif pattern=='sparse_inclusions':
        fractures=(1-smooth(.02,.11,gap))*.65
        relief=.35*macro+.18*meso
    else:raise ValueError('Unknown mineral morphology')
    masks={
        'core':smooth(.48,.78,macro)*.65,
        'patina':smooth(.46,.78,noise(n,7,seed+41))*weathering*.8,
        'fracture':np.clip(fractures*fracture_gain,0,1),
        'inclusion':(1-smooth(inclusion_radius,inclusion_radius+.10,nearest))*smooth(.30,.55,macro),
        'dirt':smooth(.52,.80,dust)*weathering*.48
    }
    color=np.zeros((n,n,3))+recipe['paletteLinear']['host']
    rough=np.full((n,n),recipe['roughness']['host']);metal=np.zeros((n,n))
    for zone in ('core','patina','fracture','inclusion','dirt'):
        weight=masks[zone]
        color=color*(1-weight[:,:,None])+np.asarray(recipe['paletteLinear'][zone])*weight[:,:,None]
        rough=rough*(1-weight)+recipe['roughness'][zone]*weight
        metal=metal*(1-weight)+(recipe['inclusionMetallic'] if zone=='inclusion' else 0)*weight
    color*=((.96+.07*meso)*(.985+.03*micro))[:,:,None]
    # Preserve the existing family factor/flat-color identity while encoding physical zone colors.
    encoded=srgb(np.clip(color/np.asarray(recipe['baseColorFactor'][:3]),0,1))
    encoded+=(hashed(np.arange(n)[None,:],np.arange(n)[:,None],seed+83)-.5)[:,:,None]/255
    rough=np.clip(rough+(micro-.5)*.06,.36,.97)
    ao=np.clip(1-.11*masks['fracture']-.035*masks['dirt']-.025*masks['patina'],.82,1)
    height=(relief+.025*(micro-.5)-.13*masks['fracture']+.08*masks['inclusion'])*recipe['reliefUv']
    dx=(np.roll(height,-1,axis=1)-np.roll(height,1,axis=1))*n/2
    dy=(np.roll(height,-1,axis=0)-np.roll(height,1,axis=0))*n/2
    dx=np.clip(dx,-.60,.60);dy=np.clip(dy,-.60,.60)
    normal=np.stack((-dx,-dy,np.ones_like(dx)),axis=2);normal/=np.linalg.norm(normal,axis=2)[:,:,None]
    layers={'base':rgba(encoded),'normal':rgba(normal*.5+.5),'orm':rgba(np.stack((ao,rough,metal),axis=2)),
            'height':rgba(np.clip(height/recipe['reliefUv'],0,1))}
    zones=np.rint(np.stack((masks['core'],masks['fracture'],masks['patina'],masks['inclusion']),axis=2)*255).astype(np.uint8)
    layers['zones']=zones
    edge=np.clip(np.arange(n),gutter,n-gutter-1)
    return {kind:data[edge[:,None],edge[None,:],:] for kind,data in layers.items()}


def descriptors(family,cfg,root):
    textures=root/'world/textures/resources';materials=root/'world/materials/resources'
    for kind in MAPS:
        write_json(textures/f'{family}_{kind}.asset.json',{
            'id':f'texture.world.resource.{family}.{kind}','type':'texture','source':f'{family}_{kind}.png',
            'textureUsage':{'base':'baseColor','normal':'normal','orm':'orm'}[kind],
            'textureColorSpace':'srgb' if kind=='base' else 'linear','textureGenerateMipmaps':True,
            'textureMaxDimension':cfg['runtimeDimensions'][kind],'textureFormat':'bc7Unorm','textureCompressionQuality':'balanced'})
    for index,variant in enumerate(cfg['surfaceVariants']):
        payload={'baseColorFactor':cfg['families'][family]['baseColorFactor'],'metallicFactor':1,'roughnessFactor':1,
                 'emissiveMultiplier':0,'uvScale':[2,2],'uvOffset':[-1.5+(index%2)/2,-1+(index//2)/2],
                 'baseColorTexture':f'texture.world.resource.{family}.base','normalTexture':f'texture.world.resource.{family}.normal',
                 'ormTexture':f'texture.world.resource.{family}.orm'}
        stem=f'{family}_{variant}';aid=f'material.world.resource.{family}.{variant}'
        write_json(materials/(stem+'.material.json'),payload)
        write_json(materials/(stem+'.asset.json'),{'id':aid,'type':'material','source':stem+'.material.json'})
        if index==0:
            write_json(materials/(family+'.material.json'),payload)
            # The existing stable material ID and descriptor path stay unchanged.
            write_json(materials/(family+'.asset.json'),{'id':f'material.world.resource.{family}','type':'material','source':family+'.material.json'})


def graph_signature():
    graphs=[]
    for mat in sorted(bpy.data.materials,key=lambda m:m.name):
        if not mat.use_nodes:continue
        nodes=[]
        for node in sorted(mat.node_tree.nodes,key=lambda n:n.name):
            defaults=[]
            for socket in node.inputs:
                if not hasattr(socket,'default_value'):continue
                value=socket.default_value
                defaults.append((socket.identifier,value if isinstance(value,(int,float,str,bool)) else list(value)))
            nodes.append((node.name,node.bl_idname,defaults,getattr(node,'blend_type',None),node.image.name if node.type=='TEX_IMAGE' else None))
        links=sorted((l.from_node.name,l.from_socket.identifier,l.to_node.name,l.to_socket.identifier) for l in mat.node_tree.links)
        graphs.append((mat.name,nodes,links))
    return hashlib.sha256(json.dumps(graphs,sort_keys=True).encode()).hexdigest()


def native_source(family,cfg,root,paths):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    scene['resource_surface_family']=family;scene['resource_surface_recipe']=json.dumps(cfg['families'][family],sort_keys=True)
    scene['edit_policy']='Paint and repack the saved image layers, or rebake the editable JSON recipe into a fresh artifact root. Preview graph edits are not exported.'
    images={}
    for kind,path in paths.items():
        image=bpy.data.images.load(str(path),check_existing=False);image.name=f'{family}:{kind}'
        image.colorspace_settings.name='sRGB' if kind=='base' else 'Non-Color';image.use_fake_user=True;image.pack()
        image.filepath=bpy.path.relpath(str(path),start=str(root/'world/authoring/resource_surfaces'))
        for packed in image.packed_files:packed.filepath=image.filepath
        images[kind]=image
    for index,variant in enumerate(cfg['surfaceVariants']):
        bpy.ops.mesh.primitive_plane_add(size=1,location=((index%2)*1.15,(index//2)*1.15,0))
        obj=bpy.context.object;obj.name='Editor swatch: '+variant
        mat=bpy.data.materials.new(f'material.world.resource.{family}.{variant}');mat.use_nodes=True
        nodes=mat.node_tree.nodes;links=mat.node_tree.links;bsdf=nodes.get('Principled BSDF')
        texcoord=nodes.new('ShaderNodeTexCoord');mapping=nodes.new('ShaderNodeVectorMath');mapping.operation='MULTIPLY_ADD'
        mapping.inputs[1].default_value=(.5,.5,1);mapping.inputs[2].default_value=((index%2)/2,(1-index//2)/2,0)
        links.new(texcoord.outputs['UV'],mapping.inputs[0])
        sampled={}
        for kind in MAPS:
            node=nodes.new('ShaderNodeTexImage');node.image=images[kind];node.label=kind;links.new(mapping.outputs[0],node.inputs['Vector']);sampled[kind]=node
        tint=nodes.new('ShaderNodeMixRGB');tint.blend_type='MULTIPLY';tint.inputs[0].default_value=1
        tint.inputs[2].default_value=cfg['families'][family]['baseColorFactor'];links.new(sampled['base'].outputs['Color'],tint.inputs[1]);links.new(tint.outputs[0],bsdf.inputs['Base Color'])
        normal=nodes.new('ShaderNodeNormalMap');links.new(sampled['normal'].outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bsdf.inputs['Normal'])
        split=nodes.new('ShaderNodeSeparateColor');links.new(sampled['orm'].outputs['Color'],split.inputs[0])
        links.new(split.outputs['Green'],bsdf.inputs['Roughness']);links.new(split.outputs['Blue'],bsdf.inputs['Metallic'])
        obj.data.materials.append(mat)
    scene['resource_surface_preview_graph']=graph_signature()
    target=root/f'world/authoring/resource_surfaces/{family}.blend';target.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(target),check_existing=False,compress=True)
    return target


def author(family,cfg,root):
    n=cfg['sourceSize']//2;atlas={kind:np.zeros((n*2,n*2,4),dtype=np.uint8) for kind in (*MAPS,'height','zones')}
    for index in range(4):
        layers=tile(cfg['families'][family],n,index,cfg['tileGutterPixels'])
        y=(index//2)*n;x=(index%2)*n
        for kind,data in layers.items():atlas[kind][y:y+n,x:x+n]=data
    paths={}
    for kind,data in atlas.items():
        path=root/(f'world/textures/resources/{family}_{kind}.png' if kind in MAPS else f'world/authoring/resource_surfaces/{family}_{kind}.png')
        path.parent.mkdir(parents=True,exist_ok=True);write_png(path,n*2,n*2,data.tobytes());paths[kind]=path
    return native_source(family,cfg,root,paths),paths


def export(family,cfg,root):
    source=AUTHOR/f'{family}.blend';bpy.ops.wm.open_mainfile(filepath=str(source))
    scene=bpy.context.scene
    if scene.get('resource_surface_family')!=family or graph_signature()!=scene.get('resource_surface_preview_graph'):
        raise ValueError('Saved family/preview graph changed; edit packed image layers or the procedural recipe, not unbaked preview nodes')
    if json.loads(scene['resource_surface_recipe'])!=cfg['families'][family]:
        raise ValueError('Recipe changed since the packed layers were authored; use Author with a fresh artifact root to rebake')
    for mat in bpy.data.materials:
        if not mat.use_nodes:continue
        for node in mat.node_tree.nodes:
            if node.type=='VECT_MATH' and node.operation!='MULTIPLY_ADD' or node.type=='NORMAL_MAP' and node.space!='TANGENT' or node.type=='SEPARATE_COLOR' and node.mode!='RGB':
                raise ValueError('Preview operation changed; unbaked node edits cannot be exported as image layers')
            if node.type=='TEX_IMAGE' and (node.extension!='REPEAT' or node.interpolation!='Linear'):
                raise ValueError('Preview texture sampling contract changed')
    paths={}
    for kind in (*MAPS,'height','zones'):
        image=bpy.data.images.get(f'{family}:{kind}')
        if image is None or image.packed_file is None or image.is_dirty:
            raise ValueError('Save/repack all five PNG image layers in the native master before Export')
        if image.colorspace_settings.name!=('sRGB' if kind=='base' else 'Non-Color'):
            raise ValueError('Packed image color-space tag changed')
        path=root/(f'world/textures/resources/{family}_{kind}.png' if kind in MAPS else f'world/authoring/resource_surfaces/{family}_{kind}.png')
        path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(bytes(image.packed_file.data))
        if inspect_png(path)!=(cfg['sourceSize'],cfg['sourceSize'],4):raise ValueError('Packed image extent/format changed')
        paths[kind]=path
    target=root/f'world/authoring/resource_surfaces/{family}.blend';shutil.copyfile(source,target)
    return target,paths


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--action',choices=('Author','Export'),required=True)
    parser.add_argument('--output-root',type=Path)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    if bpy.app.version!=(5,2,2):raise ValueError('Use pinned Blender 5.2.2')
    cfg=json.loads((AUTHOR/'recipes.json').read_text())
    root=SOURCE
    if args.output_root:
        destination=(ROOT/args.output_root).resolve()
        if not destination.is_relative_to((ROOT/'artifacts').resolve()) or destination.exists():raise ValueError('Use a fresh artifact directory')
        root=destination/'source'
    elif args.action=='Export':raise ValueError('Saved image Export requires a fresh --output-root')
    elif any((AUTHOR/f'{family}.blend').exists() for family in cfg['families']):raise ValueError('Never overwrite saved material masters; use fresh --output-root')
    records=[]
    for family in cfg['families']:
        target,paths=(author if args.action=='Author' else export)(family,cfg,root)
        descriptors(family,cfg,root)
        records.append({'family':family,'nativeSource':target.relative_to(root).as_posix(),'nativeSha256':sha(target),
                        'layers':{kind:{'path':path.relative_to(root).as_posix(),'sha256':sha(path),'bytes':path.stat().st_size} for kind,path in paths.items()}})
    folder=root/'world/authoring/resource_surfaces';folder.mkdir(parents=True,exist_ok=True)
    if root!=SOURCE:shutil.copyfile(AUTHOR/'recipes.json',folder/'recipes.json')
    write_json(folder/'sources.json',{'schemaVersion':1,'blenderVersion':'5.2.2','numpyVersion':np.__version__,
               'recipeSha256':sha(folder/'recipes.json'),'status':'authored-not-validated','validation':'NOT EXECUTED',
               'humanArtApproval':'pending','families':records})
    print('Authored 12 PBR texture sources and four editable native material projects. No tests or builds executed.')


if __name__=='__main__':main()
