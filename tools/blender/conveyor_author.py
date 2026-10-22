"""Native editable conveyor masters; export saved geometry through the existing static/rigid compiler subset."""
import argparse,copy,json,sys
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from conveyor_models import FRAME,ROOT,Mesh,module_records,assemble,export_module,write_json,collision_for


def to_blender(p):return (p[0],-p[2],p[1])
def to_engine(p):return (p[0],p[2],-p[1])


def materials():
    material=bpy.data.materials.new('Directorate conveyor shared PBR');material.use_nodes=True
    nodes=material.node_tree.nodes;links=material.node_tree.links;bsdf=nodes.get('Principled BSDF')
    folder=ROOT/'assets/source/infrastructure/directorate/conveyors/materials'
    for filename,kind,space in [('conveyor_base.png','base','sRGB'),('conveyor_normal.png','normal','Non-Color'),('conveyor_orm.png','orm','Non-Color'),('conveyor_emissive.png','emissive','sRGB')]:
        image=bpy.data.images.load(str(folder/filename),check_existing=True);image.colorspace_settings.name=space
        image.filepath='//../materials/'+filename
        node=nodes.new('ShaderNodeTexImage');node.image=image;node.label=kind
        if kind=='base':links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
        if kind=='emissive':
            links.new(node.outputs['Color'],bsdf.inputs['Emission Color']);bsdf.inputs['Emission Strength'].default_value=.35
        if kind=='normal':
            normal=nodes.new('ShaderNodeNormalMap');links.new(node.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bsdf.inputs['Normal'])
        if kind=='orm':
            split=nodes.new('ShaderNodeSeparateColor');links.new(node.outputs['Color'],split.inputs['Color']);links.new(split.outputs['Green'],bsdf.inputs['Roughness']);links.new(split.outputs['Blue'],bsdf.inputs['Metallic'])
    return material


def author(m,output):
    bpy.ops.wm.read_factory_settings(use_empty=True);scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    material=materials();scene['conveyor_asset_id']=m['assetId'];scene['conveyor_source_frame_meters']=FRAME
    scene['conveyor_axes']='Blender +Z up/-Y forward; engine +Y up/+Z forward; authored metres, exported uniformly /18'
    for variant in m['variants']:
        state=variant['state']
        for lod in range(3):
            collection=bpy.data.collections.new(f'{state}.lod{lod}');scene.collection.children.link(collection)
            collection['conveyor_state']=state;collection['conveyor_lod']=lod
            for name,part,samples in assemble(m,state,lod):
                data=bpy.data.meshes.new(name);data.from_pydata([to_blender(p) for p in part.positions],[],[part.indices[i:i+3] for i in range(0,len(part.indices),3)]);data.update()
                uv=data.uv_layers.new(name='Shared conveyor atlas')
                for loop in data.loops:u,v=part.uvs[loop.vertex_index];uv.data[loop.index].uv=(u,1-v)
                obj=bpy.data.objects.new(name,data);collection.objects.link(obj);data.materials.append(material)
                obj['conveyor_part']=name;obj['conveyor_samples']=json.dumps(samples if state=='flowing' else None)
                if samples and state=='flowing':
                    for i,offset in enumerate(samples):obj.location=to_blender(tuple(v*FRAME for v in offset));obj.keyframe_insert(data_path='location',frame=1+i*3)
                    obj.location=(0,0,0)
            default='structural_intact' if m['role'] in ('structure','closure') else 'idle_no_input'
            collection.hide_viewport=state!=default or lod!=0;collection.hide_render=collection.hide_viewport
    collision,_=collision_for(m)
    proxies=bpy.data.collections.new('Collision and selection');scene.collection.children.link(proxies);proxies.hide_viewport=True;proxies.hide_render=True
    data=bpy.data.meshes.new('collision');data.from_pydata([to_blender(p) for p in collision.positions],[],[collision.indices[i:i+3] for i in range(0,len(collision.indices),3)]);data.update()
    uv=data.uv_layers.new(name='Shared industrial atlas')
    for loop in data.loops:
        u,v=collision.uvs[loop.vertex_index];uv.data[loop.index].uv=(u,1-v)
    obj=bpy.data.objects.new('collision',data);proxies.objects.link(obj);obj['conveyor_part']='collision';obj['conveyor_samples']='null'
    ports=bpy.data.collections.new('Ports and ground-reference origin');scene.collection.children.link(ports)
    origin=bpy.data.objects.new('origin',None);ports.objects.link(origin);origin.empty_display_type='PLAIN_AXES';origin.empty_display_size=.6
    for port in m['ports']+m.get('structuralSockets',[]):
        obj=bpy.data.objects.new(port['name'],None);ports.objects.link(obj);obj.location=to_blender(port['positionMeters']);obj.empty_display_size=.3;obj.empty_display_type='ARROWS';obj['conveyor_port']=json.dumps(port)
        facing={'N':(0,0,1),'S':(0,0,-1),'E':(1,0,0),'W':(-1,0,0)}.get(port.get('facing'))
        if facing:obj.rotation_mode='QUATERNION';obj.rotation_quaternion=Vector(to_blender(facing)).to_track_quat('Z','Y')
    scene.frame_start=1;scene.frame_end=25;scene.render.fps=24;scene.frame_set(1)
    # FCurves use the same linear samples as the existing runtime clip importer.
    for action in bpy.data.actions:
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        for point in curve.keyframe_points:point.interpolation='LINEAR'
    output.mkdir(parents=True,exist_ok=True);path=output/(m['slug']+'.blend')
    bpy.ops.wm.save_as_mainfile(filepath=str(path),check_existing=False,compress=True)
    return export_module(m,output/'source',saved_parts,saved_collision)


def saved_parts(m,state,lod):
    collection=bpy.data.collections.get('Collision and selection' if state=='__collision' else f'{state}.lod{lod}')
    if collection is None:raise ValueError('Saved master is missing a state/LOD collection')
    result=[]
    for obj in collection.objects:
        if obj.type!='MESH':continue
        mesh=Mesh();data=obj.data;data.calc_loop_triangles();uv=data.uv_layers.active
        if uv is None:raise ValueError('Conveyor mesh requires authored shared-atlas UVs')
        # Rest vertices are baked independently from preview action time.
        for tri in data.loop_triangles:
            for loop_index in tri.loops:
                v=data.vertices[data.loops[loop_index].vertex_index];mesh.indices.append(len(mesh.positions));mesh.positions.append(to_engine(v.co));mesh.normals.append(to_engine(tri.normal));mesh.uvs.append((uv.data[loop_index].uv[0],1-uv.data[loop_index].uv[1]))
        samples=json.loads(obj['conveyor_samples'])
        if samples:
            action=obj.animation_data.action if obj.animation_data else None
            if action is None:raise ValueError('Moving belt part requires its saved linear preview action')
            curves=[curve for layer in action.layers for strip in layer.strips for bag in strip.channelbags for curve in bag.fcurves if curve.data_path=='location']
            if len(curves)!=3:raise ValueError('Belt motion requires exactly three translation channels')
            samples=[]
            for frame in range(1,26,3):
                value=[0.,0.,0.]
                for curve in curves:value[curve.array_index]=curve.evaluate(frame)
                samples.append(tuple(x/FRAME for x in to_engine(value)))
        if any(abs(v)>1e-6 for v in obj.rotation_euler) or any(abs(v-1)>1e-6 for v in obj.scale):raise ValueError('Apply object rotation/scale to mesh before exporting')
        result.append((obj['conveyor_part'],mesh,samples))
    return sorted(result,key=lambda p:(p[0]!='frame',p[0]))


def saved_collision(m):
    return saved_parts(m,'__collision',0)[0][1],collision_for(m)[1]


def run(request):
    parser=argparse.ArgumentParser();parser.add_argument('--action',choices=['Author','Export'],default='Author');parser.add_argument('--module');parser.add_argument('--blend-file',type=Path)
    args=parser.parse_args(request['arguments']);output=Path(request['outputRoot']);records=[]
    modules=[m for m in module_records() if not args.module or m['slug']==args.module]
    if not modules:raise ValueError('Unknown conveyor module')
    for m in modules:
        print('Conveyor',args.action,m['slug'],flush=True)
        if args.action=='Author':records.append(author(m,output))
        else:
            if len(modules)!=1 or args.blend_file is None:raise ValueError('Export requires one module and its saved master')
            bpy.ops.wm.open_mainfile(filepath=str(args.blend_file))
            for port in m['ports']+m.get('structuralSockets',[]):
                obj=bpy.data.objects.get(port['name'])
                if obj is None or (Vector(to_engine(obj.location))-Vector(port['positionMeters'])).length>1e-6:raise ValueError('Saved port frame differs from the canonical module contract')
                facing={'N':(0,0,1),'S':(0,0,-1),'E':(1,0,0),'W':(-1,0,0)}.get(port.get('facing'))
                if facing and (Vector(to_engine(obj.rotation_quaternion @ Vector((0,0,1))))-Vector(facing)).length>1e-6:raise ValueError('Saved port direction differs from the canonical module contract')
            records.append(export_module(m,output/'source',saved_parts,saved_collision))
    for record in records:
        record['materialReference']='material.infrastructure.directorate.conveyor'
    write_json(output/'conveyor-production.v1.json',{'schema':'forgeline.conveyor_models','schemaVersion':1,'designSource':'conveyor-kit.v1.json','materialPackage':'conveyor-materials.v1.json','delivery':'authored source kit; no conveyor gameplay integration; manual visual, clearance, motion and LOD review outstanding','modules':records})

if __name__=='__main__':run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
