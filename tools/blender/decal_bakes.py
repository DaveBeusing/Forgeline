"""Editable geometric impressions, selected-to-active Cycles bakes and bounded decal atlas."""
import argparse
from array import array
import copy
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import read_geometry, write_glb, write_json

FAMILIES = ('tire_tracks', 'tracked_vehicle_marks', 'road_wear', 'oil_stain',
            'blast_mark', 'shell_impact', 'scorch_mark', 'concrete_crack')
SIZE = 1024
GRID = (97, 193)


def clamp(x): return max(0., min(1., x))
def smooth(x):
    x = clamp(x)
    return x*x*(3-2*x)
def gauss(x, width): return math.exp(-(x/width)**2)


def impression(family, x, y):
    """Metre-scale relief and unlit physical paint attributes; no baked lighting."""
    r = math.hypot(x, y)
    angle = math.atan2(y, x)
    grain = (math.sin(x*91+y*57)*math.sin(y*83-x*31)+math.sin(x*43-y*67))*.5
    irregular = 1 + .08*math.sin(angle*7) + .05*math.sin(angle*13+.7)
    edge = smooth((.86-r*irregular)/.16)
    color, rough = (.065, .044, .025), .9
    if family in FAMILIES[:2]:
        lane = max(gauss(x-.39, .16), gauss(x+.39, .16))
        ends = smooth((.88-abs(y))/.14)
        envelope = clamp(lane*1.4)*ends
        period = 13 if family == FAMILIES[0] else 10
        chevron = abs(math.sin(math.pi*(y*period + abs(x)*2.2)))
        tread = gauss(chevron, .32 if family == FAMILIES[0] else .42)
        height = envelope*(-.012*tread+.003*grain)
        alpha = envelope*(.32+.48*tread)
    elif family == 'road_wear':
        envelope = smooth((.8-abs(x))/.24)*smooth((.88-abs(y))/.13)
        scratch = gauss(x-.17*math.sin(y*7), .025)+gauss(x+.32+.08*math.sin(y*11), .018)
        height = envelope*(-.007*scratch+.002*grain)
        alpha = envelope*(.22+.32*clamp(scratch)+.12*(grain+1))
        color = (.13, .12, .10)
    elif family == 'oil_stain':
        blob = min(math.hypot(x*1.25,y)*irregular, math.hypot((x-.4)*2,(y+.3)*2.2))
        envelope = smooth((.82-blob)/.13)
        height = .0015*envelope*(1+.12*grain)
        alpha = envelope*(.52+.22*smooth((.6-blob)/.4))
        color, rough = (.012, .009, .006), .19+.04*grain
    elif family in ('blast_mark','shell_impact'):
        rim = gauss(r-.43, .11)
        cavity = gauss(r, .31)
        envelope = edge
        ejecta = gauss(math.sin(angle*11+r*9), .24)*smooth((r-.25)/.2)
        height = envelope*((.014 if family == 'shell_impact' else .008)*rim-.024*cavity+.004*grain*rim)
        alpha = envelope*(.24+.5*cavity+.23*rim+.12*ejecta)
        if family=='blast_mark':alpha=envelope*(.18+.35*cavity+.25*rim+.22*ejecta)
        color = (.082, .064, .042) if family == 'shell_impact' else (.027,.023,.019)
    elif family == 'scorch_mark':
        envelope = smooth((.83-abs(x+.07*math.sin(y*9)))/.19)*smooth((.88-abs(y))/.2)
        streak = gauss(x-.14*math.sin(y*4), .24)
        height = envelope*(-.002*streak+.0008*grain)
        alpha = envelope*(.22+.46*streak)*(1-.18*abs(y))
        color = (.018, .015, .012)
    else:
        crack = 0.
        for direction, bend, width in ((.2,.12,.018),(1.7,.08,.013),(2.8,.11,.015)):
            across=x*math.cos(direction)+y*math.sin(direction)
            along=-x*math.sin(direction)+y*math.cos(direction)
            crack=max(crack,gauss(across-bend*math.sin(along*7),width))
        envelope=edge
        height=envelope*(-.018*crack+.001*grain)
        alpha=envelope*(.08+.86*crack)
        color=(.09,.085,.075)
    height += .08  # Entire high source remains above the zero-height bake carrier.
    albedo=tuple(max(.001,c*(1+.13*grain)) for c in color)
    outer=smooth((.84-abs(x))/.08)*smooth((.90-abs(y))/.08)
    return height, albedo, (clamp(1-.3*alpha),clamp(rough),0.), clamp(alpha)*outer


def carrier(source, index):
    definition=json.loads((source/'world/meshes/decals/quad.asset.json').read_text(encoding='utf-8-sig'))
    # Read the audited carrier even when a candidate source has already been adopted.
    path=source/'world/meshes/sources/world_quad.gltf'
    geometry=read_geometry(path,source,definition)
    primitive=geometry['primitives'][0]
    mesh=bpy.data.meshes.new('Unit decal carrier')
    positions=primitive['attributes']['POSITION']
    mesh.from_pydata([(p[0],-p[2],p[1]) for p in positions],[],
                     [primitive['indices'][i:i+3] for i in range(0,len(primitive['indices']),3)])
    uv=mesh.uv_layers.new(name='Atlas')
    original=primitive['attributes']['TEXCOORD_0']
    for loop in mesh.loops:
        u,v=original[loop.vertex_index]
        uv.data[loop.index].uv=((index%4+u)/4,1-(index//4+v)/2)
    obj=bpy.data.objects.new('Decal carrier',mesh);bpy.context.collection.objects.link(obj)
    obj['decal_role']='carrier';obj['atlas_cell']=index
    obj['decal_document']=json.dumps(geometry['document'],separators=(',',':'))
    return obj


def high_source(family):
    nx,ny=GRID;vertices=[];colors=[];orm=[];coverage=[]
    for j in range(ny):
        for i in range(nx):
            x,y=2*i/(nx-1)-1,2*j/(ny-1)-1
            height,c,o,a=impression(family,x,y)
            vertices.append((x*.5,y*.5,height));colors.extend((*c,1));orm.extend((*o,1));coverage.extend((a,a,a,1))
    faces=[]
    for j in range(ny-1):
        for i in range(nx-1):
            k=j*nx+i;faces.append((k,k+1,k+nx+1,k+nx))
    mesh=bpy.data.meshes.new('Editable impression grid');mesh.from_pydata(vertices,[],faces)
    for name,data in (('Albedo',colors),('Orm',orm),('Coverage',coverage)):
        attribute=mesh.color_attributes.new(name=name,type='FLOAT_COLOR',domain='POINT')
        attribute.data.foreach_set('color',data)
    for face in mesh.polygons:face.use_smooth=True
    obj=bpy.data.objects.new(family+' impression',mesh);bpy.context.collection.objects.link(obj)
    obj['decal_role']='impression';obj['decal_family']=family
    material=bpy.data.materials.new('Physical bake attributes');material.use_nodes=True
    nodes=material.node_tree.nodes;nodes.clear()
    output=nodes.new('ShaderNodeOutputMaterial');emission=nodes.new('ShaderNodeEmission')
    attribute=nodes.new('ShaderNodeAttribute');attribute.attribute_name='Albedo'
    material.node_tree.links.new(attribute.outputs['Color'],emission.inputs['Color'])
    material.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
    mesh.materials.append(material)
    return obj,attribute


def image(name, color, floating=False):
    result=bpy.data.images.new(name,width=SIZE,height=SIZE,alpha=True,float_buffer=floating)
    result.generated_color=color;result.alpha_mode='STRAIGHT';result.colorspace_settings.name='Non-Color'
    return result


def export_carrier(obj,path):
    """Export actual editable saved vertices/UVs with an explicit engine tangent basis."""
    index=int(obj['atlas_cell']);mesh=obj.data
    positions=[tuple((v.co.x,v.co.z,-v.co.y)) for v in mesh.vertices]
    normals=[tuple((v.normal.x,v.normal.z,-v.normal.y)) for v in mesh.vertices]
    uvs=[None]*len(positions)
    for loop in mesh.loops:
        u,v=mesh.uv_layers.active.data[loop.index].uv
        value=(u*4-index%4,(1-v)*2-index//4)
        if uvs[loop.vertex_index] is not None:
            assert all(abs(a-b)<1e-6 for a,b in zip(value,uvs[loop.vertex_index]))
        uvs[loop.vertex_index]=value
    indices=[v for face in mesh.polygons for v in face.vertices]
    a,b,c=indices[:3];edge1=Vector(positions[b])-Vector(positions[a]);edge2=Vector(positions[c])-Vector(positions[a])
    du1,dv1=uvs[b][0]-uvs[a][0],uvs[b][1]-uvs[a][1];du2,dv2=uvs[c][0]-uvs[a][0],uvs[c][1]-uvs[a][1]
    determinant=du1*dv2-du2*dv1;assert abs(determinant)>1e-8
    tangent=(edge1*dv2-edge2*dv1)/determinant;bitangent=(edge2*du1-edge1*du2)/determinant
    tangents=[]
    for normal in normals:
        n=Vector(normal);t=(tangent-n*n.dot(tangent)).normalized()
        tangents.append((*t,-1. if n.cross(t).dot(bitangent)<0 else 1.))
    document=json.loads(obj['decal_document'])
    geometry={'document':document,'primitives':[{'mesh':0,'primitive':0,'name':document['meshes'][0].get('name','mesh_0'),'materialSlot':-1,
        'attributes':{'POSITION':positions,'NORMAL':normals,'TEXCOORD_0':uvs,'TANGENT':tangents},'indices':indices}]}
    write_glb(path,geometry)
    return geometry


def bake_family(source,out,family,index,existing=False):
    if not existing:bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=1;scene.cycles.seed=2026
    scene.render.bake.use_selected_to_active=True;scene.render.bake.cage_extrusion=.16;scene.render.bake.max_ray_distance=.3;scene.render.bake.margin=2
    if existing:
        low=next(o for o in scene.objects if o.get('decal_role')=='carrier')
        high=next(o for o in scene.objects if o.get('decal_role')=='impression')
        attribute=next(n for n in high.data.materials[0].node_tree.nodes if n.type=='ATTRIBUTE')
        high.hide_set(False);high.hide_render=False;low.data.materials.clear()
    else:low=carrier(source,index);high,attribute=high_source(family)
    target=bpy.data.materials.new('Baked atlas receiver');target.use_nodes=True;low.data.materials.append(target)
    node=target.node_tree.nodes.new('ShaderNodeTexImage');target.node_tree.nodes.active=node
    maps={name:image(family+' '+name,default,True) for name,default in
          (('base',(0,0,0,0)),('normal',(.5,.5,1,1)),('orm',(1,1,0,1)),('coverage',(0,0,0,1)))}
    bpy.ops.object.select_all(action='DESELECT');low.select_set(True);high.select_set(True);bpy.context.view_layer.objects.active=low
    for name,attr in (('base','Albedo'),('orm','Orm'),('coverage','Coverage'),('normal',None)):
        node.image=maps[name]
        if attr:attribute.attribute_name=attr
        bpy.ops.object.bake(type='NORMAL' if name=='normal' else 'EMIT',use_clear=True,
                            normal_space='TANGENT',normal_r='POS_X',normal_g='NEG_Y',normal_b='POS_Z')
    data={name:array('f',[0.])*(SIZE*SIZE*4) for name in maps}
    for name,item in maps.items():item.pixels.foreach_get(data[name])
    channel_ranges={name:[min(values[0::4]),max(values[0::4])] for name,values in data.items()}
    assert channel_ranges['coverage'][1]>.1,channel_ranges
    for k in range(0,len(data['base']),4):
        data['base'][k+3]=clamp(data['coverage'][k])
        for c in range(3):
            value=clamp(data['base'][k+c])
            data['base'][k+c]=12.92*value if value<=.0031308 else 1.055*value**(1/2.4)-.055
    paths={}
    for name in ('base','normal','orm'):
        item=image(family+' saved '+name,(0,0,0,0))
        item.colorspace_settings.name='sRGB' if name=='base' else 'Non-Color'
        item.pixels.foreach_set(data[name]);maps[name]=item
        item.filepath_raw=str(out/f'{family}-{name}.png');item.file_format='PNG';item.save();item.pack()
        paths[name]=item.filepath_raw
    # Preview uses straight alpha and reverses only the engine normal-map green convention.
    nodes=target.node_tree.nodes;links=target.node_tree.links;nodes.clear()
    output=nodes.new('ShaderNodeOutputMaterial');shader=nodes.new('ShaderNodeBsdfPrincipled');links.new(shader.outputs[0],output.inputs['Surface'])
    textures={}
    for name in ('base','normal','orm'):
        tex=nodes.new('ShaderNodeTexImage');tex.image=maps[name];tex.label=name;textures[name]=tex
    links.new(textures['base'].outputs['Color'],shader.inputs['Base Color']);links.new(textures['base'].outputs['Alpha'],shader.inputs['Alpha'])
    split=nodes.new('ShaderNodeSeparateColor');links.new(textures['orm'].outputs['Color'],split.inputs['Color'])
    links.new(split.outputs['Green'],shader.inputs['Roughness']);links.new(split.outputs['Blue'],shader.inputs['Metallic'])
    separate=nodes.new('ShaderNodeSeparateColor');combine=nodes.new('ShaderNodeCombineColor');flip=nodes.new('ShaderNodeMath');flip.operation='SUBTRACT';flip.inputs[0].default_value=1
    links.new(textures['normal'].outputs['Color'],separate.inputs[0]);links.new(separate.outputs['Green'],flip.inputs[1])
    links.new(separate.outputs['Red'],combine.inputs['Red']);links.new(flip.outputs[0],combine.inputs['Green']);links.new(separate.outputs['Blue'],combine.inputs['Blue'])
    normal=nodes.new('ShaderNodeNormalMap');links.new(combine.outputs[0],normal.inputs['Color']);links.new(normal.outputs[0],shader.inputs['Normal'])
    high.hide_render=True;high.hide_set(True);attribute.attribute_name='Albedo'
    scene['decal_family']=family;scene['atlas_cell']=index;scene['bake_normal_green']='NEG_Y';scene['coverage_contract']='straight-alpha ordered coverage';scene['source_grid']=list(GRID)
    geometry=export_carrier(low,out/f'{family}.glb')
    # Compare the real normal bake with the saved impression in the exact runtime TBN.
    primitive=geometry['primitives'][0];base_normal=Vector(primitive['attributes']['NORMAL'][0])
    tangent=Vector(primitive['attributes']['TANGENT'][0][:3]);bitangent=base_normal.cross(tangent)*primitive['attributes']['TANGENT'][0][3]
    face=low.data.polygons[0];loops=list(face.loop_indices);points=[low.data.vertices[low.data.loops[k].vertex_index].co for k in loops]
    uv=[low.data.uv_layers.active.data[k].uv.copy() for k in loops]
    p,q=points[1]-points[0],points[2]-points[0];den=p.x*q.y-q.x*p.y
    errors=[]
    for j in (32,64,96,128,160):
        for i in (16,32,48,64,80):
            vertex=high.data.vertices[j*GRID[0]+i];delta=vertex.co-points[0]
            a=(delta.x*q.y-q.x*delta.y)/den;b=(p.x*delta.y-delta.x*p.y)/den
            sample=uv[0]+a*(uv[1]-uv[0])+b*(uv[2]-uv[0])
            pixel=(int(sample.y*SIZE)*SIZE+int(sample.x*SIZE))*4
            observed=Vector([data['normal'][pixel+c]*2-1 for c in range(3)]).normalized()
            normal=Vector((vertex.normal.x,vertex.normal.z,-vertex.normal.y))
            expected=Vector((normal.dot(tangent),normal.dot(bitangent),normal.dot(base_normal))).normalized()
            errors.append(max(abs(x-y) for x,y in zip(expected,observed)))
    normal_error=max(errors)
    assert normal_error<.18,('Saved relief and engine tangent normal disagree',family,normal_error)
    retained={maps[name] for name in ('base','normal','orm')}
    for item in list(bpy.data.images):
        if item not in retained:bpy.data.images.remove(item)
    bpy.ops.wm.save_as_mainfile(filepath=str(out/f'{family}.blend'),check_existing=False,relative_remap=False,compress=True)
    return data,dict(family=family,cell=index,vertices=len(high.data.vertices),faces=len(high.data.polygons),channelRanges=channel_ranges,normalProbeCount=len(errors),normalProbeMaximumComponentError=normal_error,maps=paths,exportSha256=hashlib.sha256((out/f'{family}.glb').read_bytes()).hexdigest(),carrier=geometry)


def run(request):
    parser=argparse.ArgumentParser();parser.add_argument('--family',choices=FAMILIES);parser.add_argument('--reopen',type=Path);parser.add_argument('--rebake',action='store_true');parser.add_argument('--edit-relief',action='store_true');parser.add_argument('--pack-shared',action='store_true')
    args=parser.parse_args(request.get('arguments',[]));source=Path(request['inputRoot']);out=Path(request['outputRoot'])
    if args.reopen:
        bpy.ops.wm.open_mainfile(filepath=str(args.reopen),load_ui=False)
        low=next(o for o in bpy.context.scene.objects if o.get('decal_role')=='carrier')
        family=bpy.context.scene['decal_family']
        if args.pack_shared:
            paths={name:source/'world/textures/decals'/('decal_atlas'+('' if name=='base' else '_'+name)+'.png') for name in ('base','normal','orm')}
            for node in low.data.materials[0].node_tree.nodes:
                if node.type!='TEX_IMAGE':continue
                name=node.label;item=bpy.data.images.load(str(paths[name]),check_existing=False)
                item.colorspace_settings.name='sRGB' if name=='base' else 'Non-Color';item.pack()
                assert bytes(item.packed_file.data)==paths[name].read_bytes()
                item.filepath='//../../textures/decals/'+paths[name].name
                for packed in item.packed_files: packed.filepath=item.filepath
                node.image=item
            retained={n.image for n in low.data.materials[0].node_tree.nodes if n.type=='TEX_IMAGE'}
            for item in list(bpy.data.images):
                if item not in retained:bpy.data.images.remove(item)
            for item in retained:item.name=Path(item.filepath).name
            bpy.ops.wm.save_as_mainfile(filepath=str(out/f'{family}.blend'),check_existing=False,relative_remap=False,compress=True)
        if args.rebake:
            if args.edit_relief:
                high=next(o for o in bpy.context.scene.objects if o.get('decal_role')=='impression')
                # A real saved-geometry edit must alter the normal bake, never the runtime plane.
                for vertex in high.data.vertices:
                    vertex.co.z+=.012*math.exp(-((vertex.co.x-.15)/.09)**2-((vertex.co.y-.05)/.09)**2)
                high.data.update()
            _,report=bake_family(source,out,family,int(low['atlas_cell']),existing=True)
            write_json(out/'rebake.json',dict(success=True,editedRelief=args.edit_relief,family=family,report=report))
            return
        export_carrier(low,out/f'{family}.glb')
        assert len([o for o in bpy.context.scene.objects if o.get('decal_role')=='impression'])==1
        assert all(n.image.packed_file for n in low.data.materials[0].node_tree.nodes if n.type=='TEX_IMAGE')
        packed=[]
        for node in low.data.materials[0].node_tree.nodes:
            if node.type=='TEX_IMAGE':
                packed.append(dict(channel=node.label,colorSpace=node.image.colorspace_settings.name,filepath=node.image.filepath,packedPaths=[item.filepath for item in node.image.packed_files],sha256=hashlib.sha256(bytes(node.image.packed_file.data)).hexdigest()))
        write_json(out/'reopen.json',dict(success=True,family=family,packed=packed,exportSha256=hashlib.sha256((out/f'{family}.glb').read_bytes()).hexdigest()))
        return
    atlas={name:array('f',[0.])*(SIZE*SIZE*4) for name in ('base','normal','orm')}
    reports=[]
    for index,family in enumerate(FAMILIES):
        if args.family and family!=args.family:continue
        data,report=bake_family(source,out,family,index);reports.append(report)
        # Image buffers are bottom-up. Material cell zero is the top-left cell.
        for y in range(SIZE//2):
            row=(1-index//4)*(SIZE//2)+y
            start=(row*SIZE+(index%4)*(SIZE//4))*4;end=start+SIZE
            for name in atlas:atlas[name][start:end]=data[name][start:end]
    for name,pixels in atlas.items():
        result=image('Shared decal '+name,(0,0,0,0))
        result.colorspace_settings.name='sRGB' if name=='base' else 'Non-Color'
        result.pixels.foreach_set(pixels)
        result.filepath_raw=str(out/f'decal_atlas{ "" if name=="base" else "_"+name}.png');result.file_format='PNG';result.save()
    if len(reports)==8:
        (out/'decal_quad.glb').write_bytes((out/f'{FAMILIES[0]}.glb').read_bytes())
    write_json(out/'bakes.json',dict(success=True,blender=bpy.app.version_string,families=reports,atlasSize=SIZE,alphaMode='straight',normalGreen='negative-engine-bitangent',originalArtwork=True))


if __name__=='__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
