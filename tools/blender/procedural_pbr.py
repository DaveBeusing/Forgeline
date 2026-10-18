"""Pinned CPU Cycles baking into immutable, atlas-compatible source generations."""
import argparse
import copy
import json
import os
import shutil
import sys
import time
import uuid
from pathlib import Path
import bpy
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from asset_contract import Catalog, read_geometry, require, write_json
from production_contract import validate_surface, geometry_stats
from pbr_contract import validate, inputs, digest, generation_key, cache_valid, MAPS, SURFACES, DEFINITIONS
from pbr_png import inspect_png, write_png


def rgba(path):
    w,h,c=inspect_png(path)
    image=bpy.data.images.load(str(path),check_existing=False)
    try:
        image.colorspace_settings.name='Non-Color'
        require(tuple(image.size)==(w,h),f'{path}: image decoding failed')
        pixels=np.empty(w*h*4,np.float32);image.pixels.foreach_get(pixels)
        require(np.all(np.isfinite(pixels)) and np.min(pixels)>=0 and np.max(pixels)<=1,f'{path}: invalid image sample')
        a=np.rint(pixels.reshape(h,w,4)*255).astype(np.uint8)[::-1].copy()
        require(np.all(a[:,:,3]==255),f'{path}: opaque atlas alpha required')
        return a
    finally:bpy.data.images.remove(image)


def semantic(kind,pixels):
    require(pixels.shape==(4096,4096,4) and pixels.dtype==np.uint8,'Invalid master extent/channels')
    require(np.all(pixels[:,:,3]==255),'Invalid atlas alpha')
    if kind=='normal':
        n=pixels[:,:,:3].astype(np.float32)/127.5-1
        length=np.linalg.norm(n,axis=2)
        require(np.all((length>.97)&(length<1.03)) and np.min(n[:,:,2])>0,'Invalid tangent normal orientation/length')


def material(family,recipe,seed):
    mat=bpy.data.materials.new('Recipe:'+family);mat.use_nodes=True
    nodes=mat.node_tree.nodes;nodes.clear();links=mat.node_tree.links
    out=nodes.new('ShaderNodeOutputMaterial');bsdf=nodes.new('ShaderNodeBsdfPrincipled');links.new(bsdf.outputs[0],out.inputs['Surface'])
    uv=nodes.new('ShaderNodeTexCoord');noise=nodes.new('ShaderNodeTexNoise');noise.noise_dimensions='4D'
    noise.inputs['Scale'].default_value=recipe['frequency'];noise.inputs['W'].default_value=(seed+recipe['cell']*97)%10000/100
    noise.inputs['Detail'].default_value=2;noise.inputs['Roughness'].default_value=.5;links.new(uv.outputs['UV'],noise.inputs['Vector'])
    # Reflectance variation is independent of explicit roughness and metal properties.
    ramp=nodes.new('ShaderNodeValToRGB')
    for element,factor in zip(ramp.color_ramp.elements,(.93,1.07)):
        element.color=(*(min(1,c*factor) for c in recipe['baseLinear']),1)
    links.new(noise.outputs['Fac'],ramp.inputs[0]);links.new(ramp.outputs[0],bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value=recipe['roughness'];bsdf.inputs['Metallic'].default_value=recipe['metallic']
    bump=nodes.new('ShaderNodeBump');bump.inputs['Distance'].default_value=recipe['heightMetres'];bump.inputs['Strength'].default_value=1
    links.new(noise.outputs['Fac'],bump.inputs['Height']);links.new(bump.outputs['Normal'],bsdf.inputs['Normal'])
    # No object cavity is represented by this flat swatch: AO is explicitly neutral.
    orm=nodes.new('ShaderNodeCombineColor');orm.mode='RGB'
    orm.inputs['Red'].default_value=1;orm.inputs['Green'].default_value=recipe['roughness'];orm.inputs['Blue'].default_value=recipe['metallic']
    emit=nodes.new('ShaderNodeEmission');emit.inputs['Strength'].default_value=1
    target=nodes.new('ShaderNodeTexImage');nodes.active=target
    mat['recipe']=json.dumps(recipe,sort_keys=True);mat['seed']=seed
    return mat,out,bsdf,ramp,orm,emit,target


def bake(family,recipe,seed,folder):
    scene=bpy.context.scene
    bpy.ops.object.select_all(action='DESELECT')
    bpy.ops.mesh.primitive_plane_add(size=1,location=(0,0,0));obj=bpy.context.object
    obj.name='Metre swatch:'+family;obj.data.uv_layers.active.name='UV0'
    obj.data.calc_tangents(uvmap='UV0')
    require(all(abs(loop.bitangent_sign-1)<1e-6 for loop in obj.data.loops),'Swatch tangent handedness differs')
    mat,out,bsdf,base,orm,emit,target=material(family,recipe,seed);obj.data.materials.append(mat)
    result={}
    for kind in MAPS:
        image=bpy.data.images.new(family+':'+kind,width=1024,height=1024,alpha=False)
        image.colorspace_settings.name='sRGB' if kind=='base' else 'Non-Color';target.image=image;mat.node_tree.nodes.active=target
        links=mat.node_tree.links
        if kind=='normal':links.new(bsdf.outputs[0],out.inputs['Surface'])
        else:
            links.new((base if kind=='base' else orm).outputs[0],emit.inputs['Color']);links.new(emit.outputs[0],out.inputs['Surface'])
        bpy.ops.object.bake(type='NORMAL' if kind=='normal' else 'EMIT',normal_space='TANGENT',normal_r='POS_X',normal_g='POS_Y',normal_b='POS_Z',use_clear=True,margin=0)
        path=folder/(family+'-'+kind+'.png');image.filepath_raw=str(path);image.file_format='PNG';image.save()
        pixels=rgba(path)
        # Constant extension guards each island at every retained mip. Existing 74px UV inset remains unchanged.
        index=np.clip(np.arange(1024),64,959);pixels=pixels[index[:,None],index[None,:],:]
        if kind=='orm':
            expected=np.rint(np.array([1,recipe['roughness'],recipe['metallic']])*255).astype(int)
            require(np.max(np.abs(pixels[:,:,:3].astype(int)-expected))<=1,f'{family}: ORM bake/linear channel mismatch')
        if kind=='normal' and recipe['heightMetres']==0:
            require(np.max(np.abs(pixels[:,:,:3].astype(int)-[128,128,255]))<=1,'Flat tangent normal differs')
        result[kind]=pixels
        image.use_fake_user=True;image.use_fake_user=True;image.pack()
    links.new(bsdf.outputs[0],out.inputs['Surface'])
    obj.hide_render=True;obj.hide_set(True)
    return result


def closure(catalog):
    # The old static importer rejects clip references. Preserve and copy the current dependency closure without editing definitions.
    pending=['unit.directorate.main_battle_tank','unit.directorate.main_battle_tank.turret'];ids=set()
    while pending:
        aid=pending.pop()
        if aid in ids:continue
        require(aid in catalog.definitions,f'Missing pilot dependency {aid}');ids.add(aid);d=catalog.definitions[aid][1]
        pending+=list(d.get('dependencies',[]))+list(d.get('materialReferences',[]))+list(d.get('textureReferences',[]))+list(d.get('animationReferences',[]))
        pending += [x['assetId'] for x in d.get('lods',[])]
        if d.get('collisionReference'):pending.append(d['collisionReference'])
        if d['type']=='material':
            m=json.loads(catalog.source(aid).read_text(encoding='utf-8-sig'));pending += [m[k] for k in ('baseColorTexture','normalTexture','ormTexture','emissiveTexture') if m.get(k)]
    ids=sorted(ids);catalog.validate(ids)
    return ids


def publish_pointer(out, value):
    temporary=out/('current-'+uuid.uuid4().hex+'.tmp');write_json(temporary,value);os.replace(temporary,out/'current.json')


def main():
    p=argparse.ArgumentParser();p.add_argument('--request',required=True);request=json.loads(Path(p.parse_args(sys.argv[sys.argv.index('--')+1:]).request).read_text())
    a=argparse.ArgumentParser();a.add_argument('--profile',required=True);a.add_argument('--families',default='painted_armor');a.add_argument('--check',action='store_true');a.add_argument('--render',action='store_true');args=a.parse_args(request['arguments'])
    config=validate(json.loads(Path(args.profile).read_text(encoding='utf-8-sig')));selected=args.families.split(',')
    require(selected and len(selected)==len(set(selected)) and set(selected)<=config['recipes'].keys(),'Invalid/duplicate selected families')
    require(bpy.app.version==(5,2,2),'Pinned Blender 5.2.2 required')
    source=Path(request['inputRoot']).resolve();out=Path(request['outputRoot']).resolve();repo=Path(__file__).resolve().parents[2]
    require(out.is_relative_to(repo/'artifacts') and not out.is_relative_to(source) and not source.is_relative_to(out),'Output must be isolated under repository artifacts')
    records=inputs(source,config);catalog=Catalog(source);ids=closure(catalog);signature,files=catalog.signature(ids);records.update(files)
    geometries={aid:read_geometry(catalog.source(aid),source,catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type']=='mesh' and '.collision' not in aid}
    for aid,g in geometries.items():validate_surface(g)
    toolchain={'version':bpy.app.version_string,'build':bpy.app.build_hash.decode(),'device':'CPU','samples':1,'threads':1,'normal':'OpenGL +Y; game importer flips V and bitangent sign'}
    code={p.name:digest(p) for p in Path(__file__).parent.glob('*.py')};toolchain['renderPreviews']=args.render
    key=generation_key(config,selected,records,toolchain,code);destination=out/'generations'/key
    write_json(out/'inspection.json',{'key':key,'selected':selected,'inputHashes':records,'toolchain':toolchain,'geometry':{aid:geometry_stats(g) for aid,g in geometries.items()},'overlapPolicy':'Existing intentionally stacked per-cell component UVs retained byte-for-byte; no repack','uvInset':.018,'terminalMip':128,'terminalInsetTexels':2.304})
    if args.check:return
    if cache_valid(destination,key):
        publish_pointer(out,{'key':key,'generation':str(destination),'cached':True});return
    require(not destination.exists(),'Corrupt cache generation: retain for diagnosis; choose a fresh output root')
    stage=out/('staging-'+uuid.uuid4().hex);stage.mkdir();started=time.monotonic()
    try:
        catalog.copy_to(ids,stage/'source');write_json(stage/'recipe.json',config)
        bpy.ops.wm.read_factory_settings(use_empty=True);scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
        scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=1;scene.cycles.seed=config['seed'];scene.cycles.use_adaptive_sampling=False;scene.render.threads_mode='FIXED';scene.render.threads=1
        scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth='8';scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.view_settings.gamma=1
        maps={kind:rgba(source/SURFACES/json.loads((source/SURFACES/DEFINITIONS[kind]).read_text())['source']) for kind in MAPS}
        for family in sorted(selected):
            r=config['recipes'][family];baked=bake(family,r,config['seed'],stage);row,col=divmod(r['cell'],4)
            for kind in MAPS:maps[kind][row*1024:(row+1)*1024,col*1024:(col+1)*1024]=baked[kind]
        for kind,pixels in maps.items():
            semantic(kind,pixels);d=json.loads((source/SURFACES/DEFINITIONS[kind]).read_text());path=stage/'source'/SURFACES/d['source'];write_png(path,4096,4096,pixels.tobytes())
            require(np.array_equal(rgba(path),pixels),'PNG roundtrip mismatch')
        for kind in MAPS:
            path=stage/'source'/SURFACES/DEFINITIONS[kind];definition=json.loads(path.read_text());cap=config['runtimeCaps'][kind]
            if cap!=definition['textureMaxDimension']:
                definition['textureMaxDimension']=cap;definition['textureMaxMipLevels']=int(np.log2(cap//128))+1;write_json(path,definition)
        # Packed source images keep the procedural graph and canonical masters editable offline.
        for kind in MAPS:
            d=json.loads((source/SURFACES/DEFINITIONS[kind]).read_text());image=bpy.data.images.load(str(stage/'source'/SURFACES/d['source']));image.colorspace_settings.name='sRGB' if kind=='base' else 'Non-Color';image.use_fake_user=True;image.pack()
        bpy.ops.wm.save_as_mainfile(filepath=str(stage/'materials.blend'),compress=True)
        if args.render:
            from roundtrip import import_scene
            from production import surface_preview, previews
            candidate=Catalog(stage/'source');import_scene(candidate,ids,[{'root':'unit.directorate.main_battle_tank'}],geometries)
            surface_preview(candidate,ids)
            previews([{'root':'unit.directorate.main_battle_tank'}],geometries,stage,True)
        source_hashes={str(path.relative_to(stage)).replace('\\','/'):digest(path) for path in sorted((stage/'source').rglob('*')) if path.is_file()}
        all_hashes={str(path.relative_to(stage)).replace('\\','/'):digest(path) for path in stage.rglob('*') if path.is_file()}
        write_json(stage/'generation.json',{'key':key,'toolchain':toolchain,'selected':selected,'sourceSignature':signature,'sourceFiles':source_hashes,'files':all_hashes,'seconds':time.monotonic()-started,'atlasPayloadBytes':sum(sum((max(128,cap>>level)**2) for level in range(int(np.log2(cap//128))+1)) for cap in config['runtimeCaps'].values()),'artApproval':'pending'})
        require(all(digest(source/relative)==expected for relative,expected in records.items()),'Authoring input changed during bake');destination.parent.mkdir(exist_ok=True);stage.rename(destination)
        publish_pointer(out,{'key':key,'generation':str(destination),'cached':False})
    except Exception as error:
        write_json(stage/'failure.json',{'error':str(error),'key':key});raise


if __name__=='__main__':main()
