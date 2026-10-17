"""Bounded Blender surface preparation over the existing source import contract."""
import argparse
import copy
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from asset_contract import Catalog, fixture_selection, read_geometry, write_glb, write_json, require, sha, blender_to_game
from production_contract import gate, geometry_stats, texture_cost, retarget_geometry
from roundtrip import import_scene, export_scene, pivot_of, parts_for


def validate_job(job):
    allowed={'sourceId','ratio','method','bevelMetres','bevelSegments','weightedNormals','protectedParts','limits'}
    require(not set(job)-allowed,'Unknown production option')
    ratio=job.get('ratio',1);width=job.get('bevelMetres',0);segments=job.get('bevelSegments',1)
    require(isinstance(ratio,(int,float)) and math.isfinite(ratio) and 0<ratio<=1,'Invalid decimation ratio')
    require(isinstance(width,(int,float)) and math.isfinite(width) and 0<=width<=.05,'Invalid bevel width in canonical metres')
    require(isinstance(segments,int) and 1<=segments<=3,'Bevel segments must be 1..3')
    require(job.get('method','COLLAPSE') in ('COLLAPSE','DISSOLVE'),'Unsupported decimation method')
    require(isinstance(job.get('weightedNormals',False),bool),'Weighted normals must be boolean')
    require(isinstance(job.get('protectedParts',[]),list) and all(isinstance(x,str) for x in job.get('protectedParts',[])),'Invalid protected part names')


def prepare_geometry(aid,definition,geometry,job):
    """Evaluate each original part separately; pivots and material boundaries cannot merge."""
    validate_job(job)
    objects=sorted((o for o in bpy.context.scene.objects if o.get('fl_role')=='geometry' and o.get('fl_asset')==aid),key=lambda o:(o['fl_primitive'],o['fl_part']))
    parts=parts_for(definition,geometry)
    names={p['name'] for p in parts}
    require(set(job.get('protectedParts',[]))<=names,'Unknown protected part')
    # Moving bindings are always protected, including descendants of an animated parent.
    protected=set(job.get('protectedParts',[]))|{p['name'] for p in parts if p.get('socket')}
    result=copy.deepcopy(geometry)
    for p in result['primitives']:p['indices']=[];p['attributes']={'POSITION':[],'NORMAL':[],'TEXCOORD_0':[],'TANGENT':[]}
    vertex_maps=[{} for _ in result['primitives']]
    ranges={i:[] for i in range(len(parts))}
    drafts=bpy.data.collections.new('Prepared surfaces');bpy.context.scene.collection.children.link(drafts)
    for original in objects:
        part=parts[original['fl_part']];pi=original['fl_primitive'];candidate=original.copy();candidate.data=original.data.copy()
        candidate.name=original.name+':prepared';drafts.objects.link(candidate);candidate['fl_role']='prepared'
        editable=part['name'] not in protected
        if editable and job.get('bevelMetres',0)>0:
            m=candidate.modifiers.new('Calibrated metre bevel','BEVEL');m.width=job['bevelMetres'];m.segments=job.get('bevelSegments',1);m.affect='EDGES';m.limit_method='ANGLE';m.harden_normals=True
        if editable and (job.get('ratio',1)<1 or job.get('method')=='DISSOLVE'):
            m=candidate.modifiers.new('Seam and material bounded reduction','DECIMATE');m.decimate_type=job.get('method','COLLAPSE')
            if m.decimate_type=='COLLAPSE':m.ratio=job.get('ratio',1);m.use_collapse_triangulate=True
            else:m.angle_limit=math.radians(.1);m.delimit={'NORMAL','MATERIAL','SEAM','UV'}
        if editable and job.get('weightedNormals',False):
            m=candidate.modifiers.new('Weighted surface normals','WEIGHTED_NORMAL');m.keep_sharp=True
        m=candidate.modifiers.new('Export triangles','TRIANGULATE');m.keep_custom_normals=True
        bpy.context.view_layer.update();evaluated=candidate.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh(preserve_all_data_layers=True,depsgraph=bpy.context.evaluated_depsgraph_get())
        try:
            require(mesh.uv_layers.active is not None,'Modifier removed UV data');mesh.calc_tangents(uvmap=mesh.uv_layers.active.name)
            out=result['primitives'][pi];attrs=out['attributes'];pivot=pivot_of(definition,part)
            start=len(out['indices'])
            for face in mesh.polygons:
                require(face.loop_total==3,'Modifier left non-triangle geometry')
                require(face.material_index==max(0,geometry['primitives'][pi]['materialSlot']),'Modifier changed a material boundary')
                for loop_index in face.loop_indices:
                    loop=mesh.loops[loop_index];point=blender_to_game(mesh.vertices[loop.vertex_index].co)
                    position=tuple(point[i]+pivot[i] for i in range(3))
                    normal=tuple(blender_to_game(mesh.corner_normals[loop_index].vector))
                    u,v=mesh.uv_layers.active.data[loop_index].uv;uv=(u,1-v)
                    tangent=(*blender_to_game(loop.tangent),-loop.bitangent_sign)
                    key=(position,normal,uv,tangent)
                    if key not in vertex_maps[pi]:
                        vertex_maps[pi][key]=len(attrs['POSITION'])
                        for name,value in zip(('POSITION','NORMAL','TEXCOORD_0','TANGENT'),key):attrs[name].append(value)
                    out['indices'].append(vertex_maps[pi][key])
            ranges[original['fl_part']].append((pi,start,len(out['indices'])-start))
        finally:evaluated.to_mesh_clear()
        candidate.hide_render=True;candidate.hide_set(True)
    updated=copy.deepcopy(definition)
    if definition.get('mechanicalParts'):
        # Existing mechanical tables describe one contiguous ordered index partition.
        require(len(result['primitives'])==1,'Prepared mechanical meshes require one production primitive')
        for i,part in enumerate(updated['mechanicalParts']):
            require(len(ranges[i])==1,'Mechanical part was split or removed')
            _,part['firstIndex'],part['indexCount']=ranges[i][0]
    return result,updated


def surface_preview(catalog,ids):
    costs=[]
    images={}
    for aid in ids:
        _,d=catalog.definitions[aid]
        if d['type']!='texture':continue
        image=bpy.data.images.load(str(catalog.source(aid)),check_existing=False)
        image.colorspace_settings.name='sRGB' if d.get('textureColorSpace')=='srgb' else 'Non-Color'
        width,height=image.size;scale=min(1,d.get('textureMaxDimension',max(width,height))/max(width,height))
        w,h=max(1,int(width*scale)),max(1,int(height*scale))
        mips=1+int(math.log2(max(w,h))) if d.get('textureGenerateMipmaps') else 1
        mips=min(mips,d.get('textureMaxMipLevels',mips))
        fmt={'bc7Unorm':'BC7','rgba8Unorm':'Rgba8'}.get(d.get('textureFormat','rgba8Unorm'))
        require(fmt is not None,'Unsupported compiled texture format')
        costs.append({'id':aid,'sourceSha256':sha(catalog.source(aid)),'usage':d.get('textureUsage'),'colorSpace':d.get('textureColorSpace'),'width':w,'height':h,'mipLevels':mips,'format':fmt,'estimatedPayloadBytes':texture_cost(w,h,fmt,mips)})
        image.pack();images[aid]=image
    for aid in ids:
        if catalog.definitions[aid][1]['type']!='material':continue
        source=json.loads(catalog.source(aid).read_text(encoding='utf-8-sig'));material=bpy.data.materials[aid];nodes=material.node_tree.nodes;links=material.node_tree.links
        uv=nodes.new('ShaderNodeTexCoord')
        scale=source.get('uvScale',[1,1]);offset=source.get('uvOffset',[0,0])
        require(len(scale)==2 and len(offset)==2 and all(math.isfinite(x) for x in scale+offset) and all(x>0 for x in scale),'Invalid material UV transform')
        multiply=nodes.new('ShaderNodeVectorMath');multiply.operation='MULTIPLY';multiply.inputs[1].default_value=(*scale,1);links.new(uv.outputs['UV'],multiply.inputs[0])
        add=nodes.new('ShaderNodeVectorMath');add.operation='ADD';add.inputs[1].default_value=(offset[0],1-scale[1]-offset[1],0);links.new(multiply.outputs[0],add.inputs[0])
        bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
        def image_node(key):
            if not source.get(key):return None
            n=nodes.new('ShaderNodeTexImage');n.image=images[source[key]];n.label=key;links.new(add.outputs[0],n.inputs['Vector']);return n
        base=image_node('baseColorTexture')
        if base:
            mix=nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1;mix.inputs[2].default_value=source.get('baseColorFactor',[1,1,1,1]);links.new(base.outputs['Color'],mix.inputs[1]);links.new(mix.outputs[0],bsdf.inputs['Base Color'])
        normal=image_node('normalTexture')
        if normal:
            n=nodes.new('ShaderNodeNormalMap');links.new(normal.outputs['Color'],n.inputs['Color']);links.new(n.outputs['Normal'],bsdf.inputs['Normal'])
        orm=image_node('ormTexture')
        if orm:
            split=nodes.new('ShaderNodeSeparateColor');links.new(orm.outputs['Color'],split.inputs[0])
            for channel,name,factor in [('Green','Roughness','roughnessFactor'),('Blue','Metallic','metallicFactor')]:
                mult=nodes.new('ShaderNodeMath');mult.operation='MULTIPLY';mult.inputs[1].default_value=source.get(factor,1);links.new(split.outputs[channel],mult.inputs[0]);links.new(mult.outputs[0],bsdf.inputs[name])
        emissive=image_node('emissiveTexture')
        if emissive:
            links.new(emissive.outputs['Color'],bsdf.inputs['Emission Color']);bsdf.inputs['Emission Strength'].default_value=source.get('emissiveMultiplier',1)
        material['fl_surface_preview']='Base/normal/ORM G roughness B metal/emissive; AO remains authoritative game input; no shader baking'
    return costs


def previews(families,geometries,output,render):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=8
    scene.render.resolution_x=384;scene.render.resolution_y=216;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('Neutral inspection world');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.3,.3,.3,1)
    light=bpy.data.lights.new('Inspection area','AREA');light.energy=350;light.shape='DISK';light.size=4
    lamp=bpy.data.objects.new('Inspection area',light);scene.collection.objects.link(lamp);lamp.location=(2,-3,5)
    captures=[]
    for family in families:
        aid=family['root'];stats=geometry_stats(geometries[aid]);lo,hi=stats['boundsMin'],stats['boundsMax'];extent=max(hi[i]-lo[i] for i in range(3));center=Vector(((lo[0]+hi[0])/2,-(lo[2]+hi[2])/2,(lo[1]+hi[1])/2))
        for obj in scene.objects:
            if obj.type=='MESH':obj.hide_render=not (obj.get('fl_role')=='geometry' and obj.get('fl_asset') in (aid,aid+'.turret'))
        for name,framing in [('close',1.1),('normal',1.8),('strategic',3.4)]:
            cam=bpy.data.cameras.new(aid+':'+name);cam.lens=50;cam.clip_start=.01;cam.clip_end=1000
            radius=.5*math.sqrt(sum((hi[i]-lo[i])**2 for i in range(3)));distance=radius/math.sin(cam.angle_y/2)*framing/extent
            obj=bpy.data.objects.new(cam.name,cam);scene.collection.objects.link(obj);obj.location=center+Vector((1,-1,1.43)).normalized()*extent*distance;obj.rotation_euler=(center-obj.location).to_track_quat('-Z','Y').to_euler();scene.camera=obj
            filename=aid+'-'+name+'.png';scene.render.filepath=str(output/filename)
            if render:bpy.ops.render.render(write_still=True)
            captures.append({'assetId':aid,'camera':name,'distanceInAssetExtents':distance,'file':filename if render else None})
    return captures


def run(request):
    out=Path(request['outputRoot']);report={'schemaVersion':1,'success':False,'errors':[]}
    try:
        parser=argparse.ArgumentParser();parser.add_argument('--profile',required=True,type=Path);parser.add_argument('--fixtures',required=True,type=Path);parser.add_argument('--render',action='store_true')
        args=parser.parse_args(request['arguments']);profile=json.loads(args.profile.read_text(encoding='utf-8-sig'))
        require(profile.get('schemaVersion')==1 and isinstance(profile.get('jobs'),dict),'Invalid surface production profile')
        catalog=Catalog(request['inputRoot']);ids,families=fixture_selection(catalog,args.fixtures,[]);signature,hashes=catalog.signature(ids)
        geometries={aid:read_geometry(catalog.source(aid),catalog.root,catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type']=='mesh'}
        collisions={catalog.definitions[aid][1].get('collisionReference') for aid in geometries}|{aid for aid in geometries if '.collision' in aid}
        require(set(profile['jobs'])<=geometries.keys(),'Production job is outside the selected source families')
        require(not set(profile['jobs'])&collisions,'Collision models cannot be surface production targets')
        import_scene(catalog,ids,families,geometries);preserved,_,_=export_scene(catalog,ids,families,geometries)
        results={};definitions={};measurements=[]
        for aid,job in profile['jobs'].items():
            source_id=job.get('sourceId',aid);require(source_id in geometries and source_id not in collisions,'Invalid LOD generation source')
            definition=catalog.definitions[aid][1];source_definition=catalog.definitions[source_id][1]
            require(source_definition.get('materialReferences',[])==definition.get('materialReferences',[]),'LOD material identity changed')
            require(source_definition.get('mechanicalParts',[])==definition.get('mechanicalParts',[]) and (not definition.get('sockets') or source_definition.get('sockets',[])==definition.get('sockets',[])),'LOD generation needs matching part/socket definitions')
            if not any((job.get('ratio',1)!=1,job.get('bevelMetres',0),job.get('weightedNormals',False),job.get('method')=='DISSOLVE')):
                validate_job(job);candidate=preserved[source_id];updated=copy.deepcopy(definition)
            else:
                candidate,prepared_definition=prepare_geometry(source_id,source_definition,geometries[source_id],job)
                updated=copy.deepcopy(definition)
                if updated.get('mechanicalParts'):updated['mechanicalParts']=prepared_definition['mechanicalParts']
            candidate=retarget_geometry(geometries[aid],candidate)
            measurements.append({'id':aid,'sourceId':source_id,**gate(geometries[aid],candidate,job['limits'])})
            results[aid]=candidate;definitions[aid]=updated
        costs=surface_preview(catalog,ids)
        require(sum(x['estimatedPayloadBytes'] for x in costs)<=profile['maxTexturePayloadBytes'],'Texture payload budget exceeded')
        source=out/'source';catalog.copy_to(ids,out/'baseline-source');catalog.copy_to(ids,source)
        for aid,g in results.items():
            path,_=catalog.definitions[aid];relative=path.relative_to(catalog.root);new=Path('_prepared')/(aid+'.glb');target=source/relative.parent/new
            write_glb(target,g);gate(geometries[aid],read_geometry(target,source,definitions[aid]),profile['jobs'][aid]['limits'])
            definitions[aid]['source']=new.as_posix();write_json(source/relative,definitions[aid])
        exported=Catalog(source);exported.validate(ids)
        for aid in ids:
            if aid not in results:
                require(sha(catalog.source(aid))==sha(exported.source(aid)),'Unselected source/collision/material/texture changed')
        inventory=[{'id':aid,**geometry_stats(g),'estimatedVertexBytes':sum(len(p['attributes']['POSITION'])*48 for p in g['primitives']),'indexBytes':sum(len(p['indices'])*4 for p in g['primitives']),'orderedMaterials':catalog.definitions[aid][1].get('materialReferences',[]),'lods':catalog.definitions[aid][1].get('lods',[])} for aid,g in sorted(geometries.items())]
        captures=previews(families,geometries,out,args.render)
        # Separate identity-preserving authoring layers instead of translating asset roots.
        for aid in geometries:
            layer=bpy.data.collections.new(aid);bpy.context.scene.collection.children.link(layer)
            for obj in list(bpy.context.scene.objects):
                if obj.get('fl_asset')==aid and obj.get('fl_role')!='prepared':
                    for collection in list(obj.users_collection):collection.objects.unlink(obj)
                    layer.objects.link(obj)
                    obj.hide_set(aid!=families[0]['root'])
        bpy.ops.wm.save_as_mainfile(filepath=str(out/'surfaces.blend'),check_existing=False)
        require(catalog.signature(ids)[0]==signature,'Production sources changed during preparation')
        report.update(success=True,sourceSignature=signature,sourceHashes=hashes,profileSha256=sha(args.profile),assetIds=ids,meshes=measurements,inventory=inventory,textures=costs,texturePayloadBytes=sum(x['estimatedPayloadBytes'] for x in costs),previews=captures,collisionSourcesUnchanged=True,productionSourcesUnchanged=True)
    except Exception as error:report['errors'].append(str(error));raise
    finally:write_json(out/'production-report.json',report)

if __name__=='__main__':run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
