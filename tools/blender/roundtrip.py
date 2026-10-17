"""Import/export the declared compiler subset through editable Blender mesh data."""
import argparse
import copy
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import (Catalog, POSITION_EPS, SOCKET_EPS, WORLD_EPS, blender_to_game,
    compare_geometry, fixture_selection, game_to_blender, geometry_stats, read_geometry,
    require, sha, socket_world, write_glb, write_json)

G2B = Matrix(((1,0,0,0),(0,0,-1,0),(0,1,0,0),(0,0,0,1)))


def tagged(name, role, aid, parent=None, data=None, **values):
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = parent
    obj['fl_role'] = role; obj['fl_asset'] = aid
    for key,value in values.items():obj['fl_'+key] = value
    obj['fl_name'] = obj.name
    return obj


def parts_for(definition, geometry):
    return definition.get('mechanicalParts') or [{'name':'whole','firstIndex':0,
        'indexCount':sum(len(p['indices']) for p in geometry['primitives']),'parent':-1}]


def slices(parts, geometry):
    start = 0
    for pi, primitive in enumerate(geometry['primitives']):
        end = start+len(primitive['indices'])
        for part_index,part in enumerate(parts):
            low=max(start,part['firstIndex']);high=min(end,part['firstIndex']+part['indexCount'])
            if low < high:yield pi,part_index,low-start,high-start,start//3
        start=end


def pivot_of(definition, part):
    socket=next((s for s in definition.get('sockets',[]) if s['name']==part.get('socket')),None)
    return tuple(socket[k] for k in 'xyz') if socket else (0,0,0)


def preview_state(material):
    bsdf=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    values={}
    for socket in bsdf.inputs:
        if hasattr(socket,'default_value'):
            value=socket.default_value
            values[socket.identifier]=value if isinstance(value,(float,int,bool,str)) else list(value)
    return json.dumps({'inputs':values,'diffuse':list(material.diffuse_color)},sort_keys=True)


def import_scene(catalog, ids, families, geometries):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1.0
    materials={}
    for aid in ids:
        _,d=catalog.definitions[aid]
        if d['type']=='material':
            source=json.loads(catalog.source(aid).read_text(encoding='utf-8-sig'))
            material=bpy.data.materials.new(aid);material.use_nodes=True;material['fl_asset']=aid
            material.diffuse_color=source.get('baseColorFactor',[1,1,1,1])
            bsdf=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
            bsdf.inputs['Base Color'].default_value=source.get('baseColorFactor',[1,1,1,1])
            bsdf.inputs['Metallic'].default_value=source.get('metallicFactor',1)
            bsdf.inputs['Roughness'].default_value=source.get('roughnessFactor',1)
            material['fl_preview']=preview_state(material)
            materials[aid]=material
    for ai,aid in enumerate(sorted(geometries)):
        _,definition=catalog.definitions[aid];geometry=geometries[aid]
        root=tagged(f'{ai:02d}:asset','root',aid)
        root['fl_logical_name']=aid
        parts=parts_for(definition,geometry);frames=[]
        for index,part in enumerate(parts):
            parent_index=part.get('parent',-1)
            parent=frames[parent_index] if parent_index>=0 else root
            frame=tagged(f'{ai:02d}:part:{part["name"]}','part',aid,parent,index=index,logical_name=part['name'])
            pivot=pivot_of(definition,part);parent_pivot=pivot_of(definition,parts[parent_index]) if parent_index>=0 else (0,0,0)
            frame.location=game_to_blender([pivot[i]-parent_pivot[i] for i in range(3)])
            frames.append(frame)
        for socket in definition.get('sockets',[]):
            empty=tagged(f'{ai:02d}:socket:{socket["name"]}','socket',aid,root,logical_name=socket['name'])
            empty.empty_display_type='ARROWS';empty.empty_display_size=.06
            empty.matrix_basis=Matrix.Translation(game_to_blender([socket[k] for k in 'xyz'])) @ G2B
        for pi,part_index,low,high,triangle_start in slices(parts,geometry):
            primitive=geometry['primitives'][pi];attributes=primitive['attributes'];part=parts[part_index]
            source_indices=primitive['indices'][low:high]
            used=sorted(set(source_indices));local={v:i for i,v in enumerate(used)}
            pivot=pivot_of(definition,part)
            vertices=[game_to_blender([attributes['POSITION'][v][i]-pivot[i] for i in range(3)]) for v in used]
            triangles=[tuple(local[v] for v in source_indices[i:i+3]) for i in range(0,len(source_indices),3)]
            mesh=bpy.data.meshes.new(f'{ai:02d}:{pi}:{part["name"]}')
            mesh.from_pydata(vertices,[],triangles);mesh.update()
            obj=tagged(mesh.name,'geometry',aid,frames[part_index],mesh,primitive=pi,part=part_index,logical_name=primitive['name'])
            for reference in definition.get('materialReferences',[]):mesh.materials.append(materials[reference])
            for polygon in mesh.polygons:polygon.material_index=max(0,primitive['materialSlot'])
            vertex_ids=mesh.attributes.new('fl_vertex','INT','POINT')
            for i,v in enumerate(used):vertex_ids.data[i].value=v
            triangle_ids=mesh.attributes.new('fl_triangle','INT','FACE')
            for i in range(len(triangles)):triangle_ids.data[i].value=triangle_start+low//3+i
            if 'TEXCOORD_0' in attributes:
                layer=mesh.uv_layers.new(name='UVMap')
                for loop in mesh.loops:
                    u,v=attributes['TEXCOORD_0'][used[loop.vertex_index]];layer.data[loop.index].uv=(u,1-v)
            if 'NORMAL' in attributes:
                normals=[game_to_blender(attributes['NORMAL'][used[loop.vertex_index]]) for loop in mesh.loops]
                mesh.normals_split_custom_set(normals)
                authored=mesh.attributes.new('fl_normal','FLOAT_VECTOR','CORNER')
                preview=mesh.attributes.new('fl_normal_preview','FLOAT_VECTOR','CORNER')
                for loop in mesh.loops:
                    authored.data[loop.index].vector=normals[loop.index]
                    preview.data[loop.index].vector=mesh.corner_normals[loop.index].vector
            if 'TANGENT' in attributes:
                tangent=mesh.attributes.new('fl_tangent','FLOAT_VECTOR','CORNER');sign=mesh.attributes.new('fl_sign','FLOAT','CORNER')
                for loop in mesh.loops:
                    t=attributes['TANGENT'][used[loop.vertex_index]];tangent.data[loop.index].vector=game_to_blender(t[:3]);sign.data[loop.index].value=t[3]
    scene['fl_contract']=json.dumps({'schemaVersion':1,'ids':ids,'families':families,'sourceSignature':catalog.signature(ids)[0]},sort_keys=True)
    bpy.context.view_layer.update()


def close_matrix(actual, expected, message, tolerance=SOCKET_EPS):
    require(max(abs(actual[i][j]-expected[i][j]) for i in range(4) for j in range(4))<=tolerance,message)


def export_scene(catalog, ids, families, geometries, geometry_comparison=None):
    scene=bpy.context.scene;bpy.context.view_layer.update()
    require(len(bpy.data.scenes)==1,'Only one authored scene is supported')
    require(not scene.animation_data and not bpy.data.actions,'Scene animation is unsupported')
    require(scene.unit_settings.system=='METRIC' and scene.unit_settings.scale_length==1.0,'Scene metre unit scale changed')
    require('fl_contract' in scene,'Missing Blender source contract')
    contract=json.loads(scene['fl_contract'])
    require(contract=={'schemaVersion':1,'ids':ids,'families':families,'sourceSignature':catalog.signature(ids)[0]},'Source/family signature changed; reimport current authoritative definitions')
    objects={};expected=set();max_socket=0.0;max_world=0.0;max_normal_encoding=0.0
    for obj in scene.objects:
        require(obj.get('fl_role') in ('root','part','socket','geometry'),'Unsupported/untracked scene object')
        require(obj.name==obj.get('fl_name'),'Logical object name changed')
        require(not obj.animation_data and not obj.constraints and not obj.modifiers and obj.instance_type=='NONE','Animation, constraints, modifiers and instances are unsupported')
        role=obj['fl_role'];aid=obj['fl_asset']
        suffix=(obj['fl_primitive'],obj['fl_part']) if role=='geometry' else obj['fl_index'] if role=='part' else obj['fl_logical_name'] if role=='socket' else None
        key=(aid,role,str(suffix));require(key not in objects,'Duplicate logical object');objects[key]=obj
    def get(aid,role,suffix=None):
        key=(aid,role,str(suffix));expected.add(key);require(key in objects,f'Missing {role}: {aid}/{suffix}');return objects[key]
    for aid in ids:
        if catalog.definitions[aid][1]['type']=='material':
            material=bpy.data.materials.get(aid)
            require(material and material.get('fl_asset')==aid and material.use_nodes,'Stable material identity/preview contract changed')
            nodes=material.node_tree.nodes;links=material.node_tree.links
            require(len(nodes)==2 and sorted(n.type for n in nodes)==['BSDF_PRINCIPLED','OUTPUT_MATERIAL'] and len(links)==1,'Unsupported material node graph')
            require(links[0].from_node.type=='BSDF_PRINCIPLED' and links[0].to_node.type=='OUTPUT_MATERIAL' and links[0].to_socket.name=='Surface','Material surface link changed')
            require(material.get('fl_preview')==preview_state(material) and not material.node_tree.animation_data,'Unsupported material preview edits')
            source=json.loads(catalog.source(aid).read_text(encoding='utf-8-sig'))
            bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
            require(max(abs(a-b) for a,b in zip(bsdf.inputs['Base Color'].default_value,source.get('baseColorFactor',[1,1,1,1])))<1e-6 and
                abs(bsdf.inputs['Metallic'].default_value-source.get('metallicFactor',1))<1e-6 and
                abs(bsdf.inputs['Roughness'].default_value-source.get('roughnessFactor',1))<1e-6,'Material preview changed; edit authoritative material source explicitly')
    results={};reports=[]
    for aid in sorted(geometries):
        _,definition=catalog.definitions[aid];baseline=geometries[aid]
        root=get(aid,'root');require(root.parent is None,'Asset root parent changed')
        close_matrix(root.matrix_world,Matrix.Identity(4),'Asset origin/axis transform changed')
        parts=parts_for(definition,baseline);frames=[]
        for i,part in enumerate(parts):
            frame=get(aid,'part',i);parent=part.get('parent',-1)
            require(frame.type=='EMPTY' and frame.parent==(frames[parent] if parent>=0 else root) and frame['fl_logical_name']==part['name'],'Part name/hierarchy changed')
            close_matrix(frame.matrix_world,Matrix.Translation(game_to_blender(pivot_of(definition,part))),'Part origin/pivot changed')
            frames.append(frame)
        socket_reports=[]
        for socket in definition.get('sockets',[]):
            empty=get(aid,'socket',socket['name']);require(empty.type=='EMPTY' and empty.parent==root,'Socket parent changed')
            baseline_position=[socket[k] for k in 'xyz']
            actual_frame=G2B.inverted() @ empty.matrix_world
            close_matrix(actual_frame,Matrix.Translation(baseline_position),'Socket world transform changed')
            actual_position=list(actual_frame.translation)
            error=max(abs(a-b) for a,b in zip(actual_position,baseline_position));max_socket=max(max_socket,error)
            family=next((f for f in families if aid in f['assets']),None)
            world_error=0.0
            if family:
                world_error=max(abs(a-b) for a,b in zip(socket_world(actual_position,family),socket_world(baseline_position,family)))
                require(world_error<=WORLD_EPS,'Gameplay socket instance-world transform changed');max_world=max(max_world,world_error)
            socket_reports.append({'name':socket['name'],'source':baseline_position,'exported':actual_position,'localError':error,'instanceWorldErrorMetres':world_error})
        exported=copy.deepcopy(baseline)
        for p in exported['primitives']:
            p['attributes']={k:[None]*len(v) for k,v in p['attributes'].items()}
        seen=set()
        for pi,part_index,low,high,triangle_start in slices(parts,baseline):
            obj=get(aid,'geometry',(pi,part_index));primitive=baseline['primitives'][pi]
            require(obj.type=='MESH' and obj.mode=='OBJECT' and obj.parent==frames[part_index] and not obj.data.shape_keys and not obj.data.animation_data,'Mesh parent/type/morph/edit mode changed')
            require(obj['fl_logical_name']==primitive['name'],'Source mesh logical name changed')
            close_matrix(obj.matrix_basis,Matrix.Identity(4),'Mesh origin/axis/scale changed')
            mesh=obj.data;refs=definition.get('materialReferences',[])
            require([m.get('fl_asset') if m else None for m in mesh.materials]==refs,'Material slot order/identity changed')
            require(len(mesh.polygons)==(high-low)//3,'Triangle count changed')
            require('fl_vertex' in mesh.attributes and 'fl_triangle' in mesh.attributes,'Missing source vertex/triangle identity')
            require(len(mesh.uv_layers)==(1 if 'TEXCOORD_0' in primitive['attributes'] else 0),'UV channels changed')
            require(not ('NORMAL' in primitive['attributes']) or mesh.has_custom_normals,'Custom normals were removed')
            by_triangle={}
            for face in mesh.polygons:
                triangle=mesh.attributes['fl_triangle'].data[face.index].value
                require(face.loop_total==3 and triangle not in by_triangle,'Only unique triangles are supported')
                by_triangle[triangle]=face
            for start in range(low,high,3):
                triangle=triangle_start+start//3;require(triangle in by_triangle,'Triangle/range identity changed')
                face=by_triangle[triangle]
                require(face.material_index==max(0,primitive['materialSlot']),'Primitive material assignment changed')
                loops=[mesh.loops[i] for i in face.loop_indices]
                vertex_ids=[mesh.attributes['fl_vertex'].data[loop.vertex_index].value for loop in loops]
                original=primitive['indices'][start:start+3]
                rotation=next((n for n in range(3) if vertex_ids[n:]+vertex_ids[:n]==original),None)
                require(rotation is not None,'Triangle winding/index identity changed')
                loops=loops[rotation:]+loops[:rotation]
                for vertex_id,loop in zip(original,loops):
                    values={'POSITION':blender_to_game(obj.matrix_world @ mesh.vertices[loop.vertex_index].co)}
                    if 'NORMAL' in primitive['attributes']:
                        require('fl_normal' in mesh.attributes and 'fl_normal_preview' in mesh.attributes,'Authored normal channels removed')
                        preview=mesh.attributes['fl_normal_preview'].data[loop.index].vector
                        require(max(abs(a-b) for a,b in zip(preview,mesh.corner_normals[loop.index].vector))<=1e-6,'Blender split-normal preview changed')
                        authored=mesh.attributes['fl_normal'].data[loop.index].vector
                        max_normal_encoding=max(max_normal_encoding,max(abs(a-b) for a,b in zip(authored,preview)))
                        values['NORMAL']=blender_to_game(authored)
                    if 'TEXCOORD_0' in primitive['attributes']:
                        u,v=mesh.uv_layers['UVMap'].data[loop.index].uv;values['TEXCOORD_0']=(u,1-v)
                    if 'TANGENT' in primitive['attributes']:
                        require('fl_tangent' in mesh.attributes and 'fl_sign' in mesh.attributes,'Authored tangent channels removed')
                        tangent=blender_to_game(mesh.attributes['fl_tangent'].data[loop.index].vector)
                        values['TANGENT']=(*tangent,mesh.attributes['fl_sign'].data[loop.index].value)
                    for semantic,value in values.items():
                        target=exported['primitives'][pi]['attributes'][semantic];old=target[vertex_id]
                        require(old is None or max(abs(a-b) for a,b in zip(old,value))<=1e-6,f'Shared vertex split changes source identity: {aid}/{semantic}/{vertex_id}')
                        if old is None:target[vertex_id]=tuple(value)
                seen.add((pi,triangle))
        for p in exported['primitives']:require(all(v is not None for a in p['attributes'].values() for v in a),'Source vertices disappeared')
        deviations=compare_geometry(baseline,exported) if geometry_comparison is None else geometry_comparison(aid,baseline,exported)
        results[aid]=exported
        reports.append({'id':aid,'sourceSha256':sha(catalog.source(aid)),'sourceStats':geometry_stats(baseline),
            'exportStats':geometry_stats(exported),'maximumDeviations':deviations,'sockets':socket_reports,
            'mechanicalPartNames':[p['name'] for p in definition.get('mechanicalParts',[])],
            'materialReferences':definition.get('materialReferences',[]),'lods':definition.get('lods',[]),
            'collisionReference':definition.get('collisionReference')})
    require(set(objects)==expected,'Unexpected asset/part/socket/geometry objects were added')
    return results,reports,{'maximumSocketLocalError':max_socket,'maximumSocketInstanceWorldErrorMetres':max_world,'maximumEditorSplitNormalEncodingError':max_normal_encoding}


def run(request):
    output=Path(request['outputRoot']);report={'schemaVersion':1,'success':False,'errors':[]}
    try:
        parser=argparse.ArgumentParser()
        parser.add_argument('--action',choices=['RoundTrip','Import','Export'],default='RoundTrip')
        parser.add_argument('--fixtures',required=True,type=Path);parser.add_argument('--asset-id',action='append',default=[])
        parser.add_argument('--blend-file',type=Path)
        args=parser.parse_args(request['arguments'])
        catalog=Catalog(request['inputRoot']);ids,families=fixture_selection(catalog,args.fixtures,args.asset_id)
        signature,source_hashes=catalog.signature(ids)
        geometries={aid:read_geometry(catalog.source(aid),catalog.root,catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type']=='mesh'}
        if args.action=='Export':
            require(args.blend_file and args.blend_file.is_file(),'Export requires an existing BlendFile')
            bpy.ops.wm.open_mainfile(filepath=str(args.blend_file.resolve()))
        else:
            require(not args.blend_file,'BlendFile is accepted only for Export')
            import_scene(catalog,ids,families,geometries)
        results,mesh_reports,deviations=export_scene(catalog,ids,families,geometries)
        if args.action!='Export':
            bpy.ops.wm.save_as_mainfile(filepath=str(output/'authoring.blend'),check_existing=False)
        if args.action!='Import':
            source_root=output/'source';catalog.copy_to(ids,output/'baseline-source');catalog.copy_to(ids,source_root)
            for aid,geometry in results.items():
                path,definition=catalog.definitions[aid];relative=path.relative_to(catalog.root)
                new_source=Path('_roundtrip')/(aid+'.glb')
                target=source_root/relative.parent/new_source
                write_glb(target,geometry)
                loaded=read_geometry(target,source_root,definition);compare_geometry(geometries[aid],loaded)
                updated=copy.deepcopy(definition);updated['source']=new_source.as_posix()
                write_json(source_root/relative,updated)
                entry=next(x for x in mesh_reports if x['id']==aid);entry['exportSha256']=sha(target)
            exported_catalog=Catalog(source_root);exported_catalog.validate(ids)
            for aid in ids:
                before=copy.deepcopy(catalog.definitions[aid][1]);after=copy.deepcopy(exported_catalog.definitions[aid][1])
                before.pop('source');after.pop('source');require(before==after,'Authoritative metadata identity changed')
        require(catalog.signature(ids)[0]==signature,'Source files changed during Blender processing')
        report.update(success=True,action=args.action,assetIds=ids,families=families,sourceSignature=signature,
            sourceHashes=source_hashes,meshes=mesh_reports,deviations=deviations,
            axes={'game':'right-handed +Y up / +Z forward','blender':'right-handed +Z up / -Y forward','mapping':'(x,y,z) -> (x,-z,y)'},
            productionSourcesUnchanged=True)
    except Exception as error:
        report['errors'].append(str(error));raise
    finally:write_json(output/'roundtrip-report.json',report)


if __name__=='__main__':
    request_path=Path(sys.argv[sys.argv.index('--request')+1])
    run(json.loads(request_path.read_text(encoding='utf-8-sig')))
