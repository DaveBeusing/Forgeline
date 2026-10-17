"""Rest-pose rigid authoring plus generated inspection poses; no clip importer."""
import argparse,copy,json,math,sys
from pathlib import Path
import bpy
from mathutils import Matrix,Quaternion,Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from asset_contract import Catalog,fixture_selection,read_geometry,write_glb,write_json,sha,require,game_to_blender
from roundtrip import import_scene,export_scene,pivot_of,G2B
from motion_contract import validate_motion,edited_definition,AXES

SAMPLES=[
 {'name':'idle'}, {'name':'forward','travel':1.7,'active':2}, {'name':'reverse','travel':-1.7,'active':2},
 {'name':'stopped','travel':1.7,'active':2}, {'name':'paused','travel':1.7,'active':2},
 {'name':'firing','active':2,'shot':1.95,'aim':.6,'elevation':.2},
 {'name':'recovery','active':2,'shot':1.85,'aim':.6,'elevation':.2},
 {'name':'producing','active':2,'door':.7}, {'name':'unpowered','active':2,'door':.7,'seconds':100},
 {'name':'destroyed','active':2,'door':.7,'disabled':True}, {'name':'lod','active':2,'door':.7,'weight':0}]


def add_properties(catalog):
    for obj in bpy.context.scene.objects:
        if obj.get('fl_role')!='part':continue
        definition=catalog.definitions[obj['fl_asset']][1]
        if not definition.get('mechanicalParts'):continue
        part=definition['mechanicalParts'][obj['fl_index']]
        for key,default in [('motion','static'),('rate',1),('travel',0)]:obj['fl_'+key]=part.get(key,default)
        obj['fl_pivot_game']=list(pivot_of(definition,part));obj['fl_axis_description']=AXES[part.get('motion','static')]
        obj.id_properties_ui('fl_rate').update(min=-20,max=20);obj.id_properties_ui('fl_travel').update(min=-2,max=2)


def author_definitions(catalog,geometries,profile):
    updates={}
    for aid,g in geometries.items():
        d=catalog.definitions[aid][1]
        if not d.get('mechanicalParts'):continue
        frames={o['fl_index']:o for o in bpy.context.scene.objects if o.get('fl_role')=='part' and o.get('fl_asset')==aid}
        edits={}
        for i,p in enumerate(d['mechanicalParts']):
            frame=frames[i]
            require(all('fl_'+key in frame for key in ('motion','rate','travel','pivot_game')),'Missing typed rigid properties')
            edits[p['name']]={'motion':frame['fl_motion'],'rate':frame['fl_rate'],'travel':frame['fl_travel'],'pivot':list(frame['fl_pivot_game'])}
        updates[aid]=edited_definition(d,edits,profile['maxPivotDeltaMetres'],sum(len(p['indices']) for p in g['primitives']))
    return updates


def apply_pivots(catalog,geometries,updates):
    for aid,d in updates.items():
        parts=d['mechanicalParts']
        for obj in bpy.context.scene.objects:
            if obj.get('fl_asset')!=aid:continue
            if obj.get('fl_role')=='part':
                i=obj['fl_index'];pivot=pivot_of(d,parts[i]);parent=parts[i].get('parent',-1)
                parent_pivot=pivot_of(d,parts[parent]) if parent>=0 else (0,0,0)
                obj.location=game_to_blender([pivot[k]-parent_pivot[k] for k in range(3)])
            elif obj.get('fl_role')=='socket':
                socket=next(s for s in d.get('sockets',[]) if s['name']==obj['fl_logical_name'])
                obj.matrix_basis=Matrix.Translation(game_to_blender([socket[k] for k in 'xyz']))@G2B
            elif obj.get('fl_role')=='geometry':
                pivot=pivot_of(d,parts[obj['fl_part']]);p=geometries[aid]['primitives'][obj['fl_primitive']]
                for v in obj.data.vertices:
                    original=obj.data.attributes['fl_vertex'].data[v.index].value
                    v.co=game_to_blender([p['attributes']['POSITION'][original][k]-pivot[k] for k in range(3)])
                obj.data.update()
                if 'fl_normal' in obj.data.attributes:
                    obj.data.normals_split_custom_set([tuple(x.vector) for x in obj.data.attributes['fl_normal'].data])
                    for loop in obj.data.loops:
                        obj.data.attributes['fl_normal_preview'].data[loop.index].vector=obj.data.corner_normals[loop.index].vector
        path,_=catalog.definitions[aid];catalog.definitions[aid]=(path,d)
    bpy.context.view_layer.update()


def compose(parts,definition,sample,world,scale):
    transforms=[];weight=sample.get('weight',1);enabled=not sample.get('disabled',False)
    s=Matrix.Diagonal((*scale,1));inv=s.inverted()
    for part in parts:
        rotation=Quaternion();translation=Vector((0,0,0));kind=part.get('motion','static');rate=part.get('rate',1);travel=part.get('travel',0)
        if enabled:
            phase=math.remainder(sample.get('active',0)*rate,math.tau);angle=0;axis=(0,1,0)
            if kind=='turretYaw':angle=sample.get('aim',0)
            elif kind=='cannon':axis=(1,0,0);angle=-max(-.15,min(.55,sample.get('elevation',0)))
            elif kind=='wheel':axis=(1,0,0);angle=math.remainder(sample.get('travel',0)*rate,math.tau)
            elif kind in ('fan','radar'):angle=phase
            elif kind=='crane':angle=math.sin(phase)*travel
            elif kind=='infantryStride':axis=(1,0,0);angle=math.sin(math.remainder(sample.get('travel',0)*rate,math.tau))*travel
            rotation=Quaternion(axis,angle)
            if kind=='door':translation.y=sample.get('door',0)*travel
            elif kind=='cannon':
                shot=sample.get('shot',-1);age=sample.get('seconds',2)-shot
                pulse=(age/.05 if age<.05 else 1-(age-.05)/.30) if shot>=0 and 0<=age<.35 else 0
                pulse=max(0,min(1,pulse));translation.z=-(pulse*pulse*(3-2*pulse))*travel
        if weight<=0:rotation=Quaternion();translation=Vector((0,0,0))
        elif weight<1:rotation=Quaternion().slerp(rotation,weight);translation*=weight
        pivot=Vector(pivot_of(definition,part))
        local=Matrix.Translation(pivot)@inv@rotation.to_matrix().to_4x4()@s@Matrix.Translation(translation)@Matrix.Translation(-pivot)
        parent=part.get('parent',-1);transforms.append((world if parent<0 else transforms[parent])@local)
    return transforms


def layers(families):
    scene=bpy.context.scene;visible={families[0]['root'],families[0]['root']+'.turret'}
    for aid in sorted({o.get('fl_asset') for o in scene.objects if o.get('fl_asset')}):
        layer=bpy.data.collections.new(aid);scene.collection.children.link(layer)
        for obj in list(scene.objects):
            if obj.get('fl_asset')!=aid:continue
            for collection in list(obj.users_collection):collection.objects.unlink(obj)
            layer.objects.link(obj);obj.hide_set(aid not in visible);obj.hide_render=aid not in visible


def preview_images(catalog,ids,families,output,render):
    from production import surface_preview
    surface_preview(catalog,ids)
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=8
    scene.render.resolution_x=384;scene.render.resolution_y=216;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('Neutral rigid inspection');require(scene.world.node_tree is not None,'Pinned Blender requires a node-based world');scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.3,.3,.3,1)
    light=bpy.data.lights.new('Rigid inspection sun','SUN');light.energy=3
    sun=bpy.data.objects.new(light.name,light);scene.collection.objects.link(sun);sun.rotation_euler=(.5,-.5,-.4)
    images=[];cameras=[]
    for family in families:
        aid=family['root'];scene.frame_set(1);points=[]
        for obj in scene.objects:
            if obj.type!='MESH':continue
            selected=obj.get('fl_asset') in (aid,aid+'.turret');obj.hide_render=not selected;obj.hide_set(not selected)
            if selected:points.extend(obj.matrix_world@v.co for v in obj.data.vertices)
        lo=Vector([min(p[i] for p in points) for i in range(3)]);hi=Vector([max(p[i] for p in points) for i in range(3)]);center=(lo+hi)/2;extent=max(hi-lo)
        camera=bpy.data.cameras.new(aid+':inspection');camera.lens=50;camera.clip_start=.01;camera.clip_end=2000
        obj=bpy.data.objects.new(camera.name,camera);scene.collection.objects.link(obj);obj.location=center+Vector((1,-1,1.4)).normalized()*extent*3.5;obj.rotation_euler=(center-obj.location).to_track_quat('-Z','Y').to_euler();scene.camera=obj;cameras.append(obj)
        for state in ('idle','firing' if aid.startswith('unit.') else 'producing'):
            index=next(i for i,sample in enumerate(SAMPLES) if sample['name']==state);scene.frame_set(1+index*10)
            name=aid+'-'+state+'.png';scene.render.filepath=str(output/name)
            if render:bpy.ops.render.render(write_still=True);images.append(name)
    scene.camera=cameras[0];scene.frame_set(1)
    for obj in scene.objects:
        if obj.type=='MESH':obj.hide_render=obj.get('fl_asset') not in (families[0]['root'],families[0]['root']+'.turret');obj.hide_set(obj.hide_render)
    return images


def inspection(catalog,ids,families,geometries,output,render):
    import_scene(catalog,ids,families,geometries);rows=[]
    scene=bpy.context.scene;scene.render.fps=20;scene.frame_start=1;scene.frame_end=len(SAMPLES)*10
    for si,sample in enumerate(SAMPLES):
        frame_number=1+si*10;scene.frame_set(frame_number)
        for aid,g in sorted(geometries.items()):
            d=catalog.definitions[aid][1];parts=d.get('mechanicalParts',[])
            if not parts:continue
            family=next(f for f in families if aid in f['assets']);scale=family['instanceScale']
            world=Matrix.Translation(family['instanceTranslation'])@Matrix.Rotation(math.radians(family['instanceYawDegrees']),4,'Y')@Matrix.Diagonal((*scale,1))
            if aid==family['root']+'.turret':
                parent=catalog.definitions[family['root']][1];pivot=next(s for s in parent['sockets'] if s['name']=='turret_pivot');point=Vector([pivot[k] for k in 'xyz'])
                s=Matrix.Diagonal((*scale,1));world=world@Matrix.Translation(point)@s.inverted()@Matrix.Rotation(sample.get('aim',0),4,'Y')@s@Matrix.Translation(-point)
            matrices=compose(parts,d,sample,world,scale)
            frames={o['fl_index']:o for o in scene.objects if o.get('fl_role')=='part' and o.get('fl_asset')==aid}
            actual=[]
            for i,matrix in enumerate(matrices):
                obj=frames[i];pivot=Vector(pivot_of(d,parts[i]));obj.rotation_mode='QUATERNION'
                obj.matrix_world=G2B@matrix@Matrix.Translation(pivot)@G2B.inverted()
                bpy.context.view_layer.update()
                observed=G2B.inverted()@obj.matrix_world@G2B@Matrix.Translation(-pivot)
                error=max(abs(matrix[r][c]-observed[r][c]) for r in range(4) for c in range(4))
                require(error<=2e-5,'Blender cannot represent this rigid hierarchy/scale within the runtime matrix tolerance')
                actual.append([observed[r][c] for c in range(4) for r in range(4)])
                for prop in ('location','rotation_quaternion','scale'):obj.keyframe_insert(data_path=prop,frame=frame_number)
            rows.append({'id':aid,'root':family['root'],'frame':frame_number,'sample':sample,'matrices':actual})
    layers(families);images=preview_images(catalog,ids,families,output,render)
    # The inspection project has generated actions. It is deliberately not an export input.
    bpy.ops.wm.save_as_mainfile(filepath=str(output/'inspection.blend'),check_existing=False)
    write_json(output/'poses.json',{'schemaVersion':1,'families':families,'samples':rows})
    return len(rows),images


def run(request):
    output=Path(request['outputRoot']);report={'schemaVersion':1,'success':False,'errors':[]}
    try:
        parser=argparse.ArgumentParser();parser.add_argument('--action',choices=['Author','Export'],default='Author');parser.add_argument('--fixtures',type=Path,required=True);parser.add_argument('--profile',type=Path,required=True);parser.add_argument('--blend-file',type=Path);parser.add_argument('--render',action='store_true')
        args=parser.parse_args(request['arguments']);profile=json.loads(args.profile.read_text(encoding='utf-8-sig'))
        require(set(profile)=={'schemaVersion','maxPivotDeltaMetres'} and profile['schemaVersion']==1,'Unsupported motion profile')
        catalog=Catalog(request['inputRoot']);ids,families=fixture_selection(catalog,args.fixtures,[]);signature,hashes=catalog.signature(ids)
        geometries={aid:read_geometry(catalog.source(aid),catalog.root,catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type']=='mesh'}
        for aid,g in geometries.items():validate_motion(catalog.definitions[aid][1],sum(len(p['indices']) for p in g['primitives']))
        if args.action=='Export':
            require(args.blend_file and args.blend_file.is_file(),'Export needs an existing rest-pose authoring project');bpy.ops.wm.open_mainfile(filepath=str(args.blend_file.resolve()))
            require('fl_motion_profile' in bpy.context.scene,'Export requires the rest authoring project, not generated inspection actions')
            require(bpy.context.scene.get('fl_motion_profile')==sha(args.profile),'Motion profile changed; reimport or restore the reviewed profile')
        else:
            require(not args.blend_file,'BlendFile is accepted only for Export');import_scene(catalog,ids,families,geometries);add_properties(catalog);bpy.context.scene['fl_motion_profile']=sha(args.profile)
        # Strict original rest-pose export rejects unreviewed topology, transforms and actions.
        export_scene(catalog,ids,families,geometries)
        updates=author_definitions(catalog,geometries,profile)
        catalog.copy_to(ids,output/'baseline-source');apply_pivots(catalog,geometries,updates)
        results,mesh_reports,deviations=export_scene(catalog,ids,families,geometries)
        if args.action=='Author':
            layers(families)
            bpy.ops.wm.save_as_mainfile(filepath=str(output/'authoring.blend'),check_existing=False)
        source=output/'source';catalog.copy_to(ids,source)
        for aid,d in updates.items():
            path,_=catalog.definitions[aid];relative=path.relative_to(catalog.root);new=Path('_rigid')/(aid+'.glb')
            write_glb(source/relative.parent/new,results[aid]);updated=copy.deepcopy(d);updated['source']=new.as_posix();write_json(source/relative,updated)
        Catalog(source).validate(ids)
        samples,images=inspection(catalog,ids,families,geometries,output,args.render)
        require(catalog.signature(ids)[0]==signature,'Source files changed during rigid authoring')
        report.update(success=True,action=args.action,sourceSignature=signature,sourceHashes=hashes,assetIds=ids,authoredDefinitions=updates,meshes=mesh_reports,deviations=deviations,poseSamples=samples,inspectionImages=images,productionSourcesUnchanged=True)
    except Exception as error:report['errors'].append(str(error));raise
    finally:write_json(output/'motion-report.json',report)

if __name__=='__main__':run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
