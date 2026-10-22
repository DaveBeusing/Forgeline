"""Author editable mineral clusters, or export edits from their saved Blender masters.

Authoring only: this does not run Blender reopening, compiler or runtime tests.
Use Blender 5.2.2 --background --factory-startup --python-exit-code 23 --python
build/resource-deposits/cluster_author.py -- --action Author (once), or Export.
"""
import argparse
import json
import math
import shutil
import sys
from pathlib import Path

import bpy
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'assets/source'
MASTERS = SOURCE / 'world/authoring/resource_clusters'
DEFINITIONS = SOURCE / 'world/meshes/resources'
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import game_to_blender, blender_to_game, read_geometry, write_glb, sha, write_json

# Independent fragment centres, aspect ratios, heights and yaw; not copies of a rotated mesh.
# The four layouts respectively form a shelf, broken ridge, radial fan and separated seam.
LAYOUTS = (
    [(-.25,-.16,.35,.26,.46,-.2),(.09,-.11,.31,.20,.70,.3),(.30,.10,.20,.23,.42,-.5),
     (-.13,.22,.27,.19,.34,.8),(.16,.29,.19,.14,.26,-.7),(-.36,.15,.14,.15,.23,.4),
     (.33,-.27,.12,.10,.19,1.1),(-.38,-.33,.08,.11,.15,-.3),(.02,.37,.10,.08,.14,.7)],
    [(-.32,-.24,.22,.19,.30,.2),(-.10,-.05,.26,.16,.76,-.4),(.08,.17,.20,.22,.57,.5),
     (.33,.32,.18,.16,.35,.9),(.27,-.26,.22,.12,.20,-.8),(-.34,.25,.13,.20,.25,.3),
     (.36,.02,.09,.13,.17,1.2),(-.12,-.37,.12,.08,.14,-.7)],
    [(-.17,.12,.25,.20,.49,-.4),(.12,-.04,.24,.17,.61,.8),(.31,-.24,.20,.14,.40,1.0),
     (-.32,-.15,.14,.21,.34,-.8),(.20,.29,.23,.13,.26,.1),(-.10,-.34,.17,.12,.28,.5),
     (-.31,.32,.13,.12,.18,-1.1),(.39,.12,.09,.12,.16,.7),(.04,.41,.11,.08,.13,-.2)],
    [(-.29,-.24,.28,.17,.59,-.7),(-.27,.13,.24,.21,.38,.4),(.24,.25,.25,.18,.64,-.2),
     (.31,-.11,.18,.25,.32,.7),(-.03,-.04,.14,.12,.23,-.9),(.03,.37,.13,.10,.19,.2),
     (.26,-.35,.13,.11,.21,-.3),(-.40,.34,.10,.10,.15,1.0)]
)


def contract():
    return json.loads((MASTERS / 'variants.json').read_text(encoding='utf-8'))


def fragment(family, item, index, lod):
    x,z,rx,rz,height,yaw = item
    sides = (6 if family == 'volatiles' else 5) if lod == 0 else 4 if lod == 1 else 3
    # Cleaved silicate plates lean sideways, not into generic pointy towers.
    if family == 'silicates':
        rx *= 1.12; rz *= .70; height *= 1.10
        rings = [(0,1,0),(.62,.88,.20),(1,.50,.34)] if lod == 0 else [(0,1,0),(1,.58,.34)]
    elif family == 'volatiles':
        rx *= 1.08; height *= .55
        rings = [(0,.88,0),(.42,1,.03),(.82,.86,-.06),(1,.47,.02)] if lod == 0 else [(0,.95,0),(1,.65,.02)]
    elif family == 'rare_elements':
        rx *= 1.06; rz *= .90; height *= .69
        rings = [(0,1,0),(.58,.94,-.08),(1,.58,.12)] if lod == 0 else [(0,1,0),(1,.58,.12)]
    else:
        height *= .75
        rings = [(0,.90,0),(.35,1,-.08),(.80,.88,.10),(1,.62,.16)] if lod == 0 else [(0,1,0),(1,.68,.16)]
    points = []
    for level,radius,lean in rings:
        for side in range(sides):
            angle = math.tau * side / sides + .11 * index
            rough = 1 + .10 * math.sin(side * 2.13 + index * .83)
            a = rx * (math.cos(angle) * radius * rough + lean)
            b = rz * math.sin(angle) * radius * (1 + .07 * math.cos(side + index))
            y = level * height * (1 + .09 * math.sin(side * 1.7 + index))
            points.append((x + a*math.cos(yaw)-b*math.sin(yaw), y,
                           z + a*math.sin(yaw)+b*math.cos(yaw)))
    faces = [tuple(reversed(range(sides)))]
    for ring in range(len(rings)-1):
        for side in range(sides):
            a=ring*sides+side; b=ring*sides+(side+1)%sides
            faces.append((a,b,b+sides,a+sides))
    faces.append(tuple((len(rings)-1)*sides+i for i in range(sides)))
    return points,faces


def parts(family, layout, size, lod):
    items = LAYOUTS[layout]
    count = min(len(items), {'small':5,'medium':7,'large':9}[size])
    # Retain the same principal anchors across LODs, with only peripheral fragments culled.
    reduced_count = 5 if family == 'rare_elements' else 7
    count = min(count, reduced_count if lod == 1 else 3 if lod == 2 else count)
    result = []
    for index,item in enumerate(items[:count]):
        result.append((f'bedrock_{index:02}', *fragment(family,item,index,lod)))
    if family == 'rare_elements':
        # Solid inclusions penetrate their host rock and have different axes/heights.
        for i in range(2 if lod == 2 else 3):
            x,z,rx,rz,h,yaw=items[i]
            inclusion=(x+.04,z-.025,rx*.28,rz*.38,h*.90,yaw+.65+i*.4)
            result.append((f'inclusion_{i:02}', *fragment('silicates',inclusion,11+i,lod)))
    return result


def material(family):
    definition = json.loads((SOURCE / f'world/materials/resources/{family}.material.json').read_text())
    mat = bpy.data.materials.new(f'material.world.resource.{family}')
    mat['fl_asset'] = mat.name; mat.diffuse_color = definition['baseColorFactor']; mat.use_nodes = True
    bsdf=mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value=definition['baseColorFactor']
    # Preview references existing shared maps. No new texture or material source is authored.
    folder=SOURCE/'materials/directorate/surfaces'
    for suffix,space in [('base','sRGB'),('normal','Non-Color'),('orm','Non-Color')]:
        path=folder/f'production_surface_{suffix}.png'
        image=bpy.data.images.load(str(path),check_existing=True); image.colorspace_settings.name=space
        image.filepath=bpy.path.relpath(str(path),start=str(MASTERS))
        node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;node.label=suffix
        if suffix=='base': mat.node_tree.links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
        elif suffix=='normal':
            normal=mat.node_tree.nodes.new('ShaderNodeNormalMap')
            mat.node_tree.links.new(node.outputs['Color'],normal.inputs['Color']);mat.node_tree.links.new(normal.outputs['Normal'],bsdf.inputs['Normal'])
        else:
            separate=mat.node_tree.nodes.new('ShaderNodeSeparateColor');mat.node_tree.links.new(node.outputs['Color'],separate.inputs['Color'])
            mat.node_tree.links.new(separate.outputs['Green'],bsdf.inputs['Roughness']);mat.node_tree.links.new(separate.outputs['Blue'],bsdf.inputs['Metallic'])
    return mat


def make_object(collection,name,points,faces,uv_range,mat):
    # Duplicated corners retain hard fracture normals and explicit triangular UV islands.
    data=bpy.data.meshes.new(name);data.from_pydata([game_to_blender(p) for p in points],[],faces);data.update()
    obj=bpy.data.objects.new(name,data);collection.objects.link(obj)
    if mat: data.materials.append(mat)
    # Recalculate outward normals once during authoring, never during saved-master export.
    import bmesh
    bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free()
    data.calc_loop_triangles()
    uv=data.uv_layers.new(name='Mineral atlas')
    lo,hi=uv_range
    for face in data.polygons:
        for j,loop_index in enumerate(face.loop_indices):
            angle=math.tau*j/face.loop_total
            u=.5+.43*math.cos(angle);v=.5+.43*math.sin(angle)
            uv.data[loop_index].uv=(lo[0]+u*(hi[0]-lo[0]),1-(lo[1]+v*(hi[1]-lo[1])))
    return obj


def author(family,cfg):
    target=MASTERS/f'{family}.blend'
    if target.exists(): raise ValueError(f'Refusing to replace editable master {target}; use Export for saved edits')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    scene['resource_cluster_contract']=json.dumps(cfg,sort_keys=True)
    scene['resource_family']=family;scene['resource_ground_y']=-.5
    old=json.loads((DEFINITIONS/f'{family}.asset.json').read_text())
    old_path=(DEFINITIONS/old['source']).resolve()
    geometry=read_geometry(old_path,SOURCE,old)
    coords=[uv for p in geometry['primitives'] for uv in p['attributes']['TEXCOORD_0']]
    uv_range=([min(p[k] for p in coords) for k in range(2)],[max(p[k] for p in coords) for k in range(2)])
    mat=material(family)
    for layout,name in enumerate(cfg['families'][family]['layouts']):
        for size in cfg['sizes']:
            levels=[parts(family,layout,size,lod) for lod in range(3)]
            all_points=[p for level in levels for _,points,_ in level for p in points]
            lo=[min(p[k] for p in all_points) for k in range(3)];hi=[max(p[k] for p in all_points) for k in range(3)]
            span={'small':.68,'medium':.86,'large':1.0}[size]
            def normalize(p):
                return ((p[0]-(lo[0]+hi[0])/2)/(hi[0]-lo[0])*span,
                        (p[1]-lo[1])/(hi[1]-lo[1])*span-.5,
                        (p[2]-(lo[2]+hi[2])/2)/(hi[2]-lo[2])*span)
            for lod,level in enumerate(levels):
                collection=bpy.data.collections.new(f'{name}.{size}.lod{lod}');scene.collection.children.link(collection)
                collection['layout']=name;collection['size']=size;collection['lod']=lod
                for part_name,points,faces in level:
                    make_object(collection,part_name,[normalize(p) for p in points],faces,uv_range,mat)
                visible=name==cfg['families'][family]['defaultLayout'] and size=='medium' and lod==0
                collection.hide_viewport=not visible;collection.hide_render=not visible
            # A conservative, material-free closed proxy; it never becomes simulation navigation.
            collection=bpy.data.collections.new(f'{name}.{size}.proxy');scene.collection.children.link(collection)
            collection['layout']=name;collection['size']=size;collection['lod']=-1
            points=[(x,y,z) for x in (-span/2,span/2) for y in (-.5,span-.5) for z in (-span/2,span/2)]
            faces=[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]
            make_object(collection,'selection_collision_envelope',points,faces,uv_range,None)
            collection.hide_viewport=True;collection.hide_render=True
    scene['axes']='Blender +Z up/-Y forward; exported engine +Y up/+Z forward; identity object transforms'
    scene.view_settings.view_transform='Standard';scene.render.film_transparent=True
    bpy.ops.wm.save_as_mainfile(filepath=str(target),check_existing=False,compress=True)


def geometry_from_collection(collection,material_id):
    values={'POSITION':[],'NORMAL':[],'TEXCOORD_0':[],'TANGENT':[]}
    for obj in sorted(collection.objects,key=lambda o:o.name):
        if obj.type!='MESH': raise ValueError('Export collection must contain only editable meshes')
        if obj.modifiers or obj.animation_data or obj.data.shape_keys or obj.matrix_world != Matrix.Identity(4):
            raise ValueError('Apply transforms/modifiers; mineral rest exports cannot contain animation')
        mesh=obj.data;mesh.calc_loop_triangles();mesh.calc_tangents(uvmap='Mineral atlas')
        expected=[material_id] if material_id else []
        if [m.get('fl_asset') if m else None for m in mesh.materials]!=expected:
            raise ValueError('Saved material slots differ from the existing family binding')
        for tri in mesh.loop_triangles:
            for index in tri.loops:
                loop=mesh.loops[index]
                values['POSITION'].append(blender_to_game(mesh.vertices[loop.vertex_index].co))
                values['NORMAL'].append(blender_to_game(tri.normal))
                u,v=mesh.uv_layers.active.data[index].uv;values['TEXCOORD_0'].append((u,1-v))
                values['TANGENT'].append((*blender_to_game(loop.tangent),-loop.bitangent_sign))
        mesh.free_tangents()
    primitive={'attributes':{k:i for i,k in enumerate(values)},'indices':4,'mode':4}
    if material_id: primitive['material']=0
    document={'asset':{'version':'2.0','generator':'ForgeLine mineral cluster exporter'},'scene':0,'scenes':[{'nodes':[0]}],
              'nodes':[{'mesh':0}],'meshes':[{'primitives':[primitive]}]}
    if material_id: document['materials']=[{'name':material_id}]
    count=len(values['POSITION'])
    return {'document':document,'primitives':[{'mesh':0,'primitive':0,'attributes':values,'indices':list(range(count))}]}


def export(family,cfg,source_root):
    material_id=f'material.world.resource.{family}';root_id=f'mesh.world.resource.{family}'
    output=source_root/'world/meshes/sources/clusters'
    definitions=source_root/'world/meshes/resources'
    expected_collections=[bpy.data.collections[f'{layout}.{size}.{suffix}']
        for layout in cfg['families'][family]['layouts'] for size in cfg['sizes']
        for suffix in ('proxy','lod0','lod1','lod2')]
    expected_objects={obj for collection in expected_collections for obj in collection.objects}
    if set(bpy.context.scene.objects)!=expected_objects or bpy.context.scene.animation_data or bpy.data.actions:
        raise ValueError('Saved scene contains unexpected objects or animation')
    if any(len(obj.users_collection)!=1 for obj in expected_objects):
        raise ValueError('Each editable fragment must belong to exactly one variant collection')
    rows=[]
    for layout in cfg['families'][family]['layouts']:
        for size in cfg['sizes']:
            base=f'{root_id}.{layout}.{size}'
            proxy_id=base+'.collision'
            for lod in (-1,0,1,2):
                suffix='proxy' if lod==-1 else f'lod{lod}'
                collection=bpy.data.collections[f'{layout}.{size}.{suffix}']
                geometry=geometry_from_collection(collection,None if lod==-1 else material_id)
                if bpy.context.scene.get('resource_family')!=family or bpy.context.scene.unit_settings.scale_length!=1 or bpy.context.scene.unit_settings.system!='METRIC':
                    raise ValueError('Saved master family or metre-unit contract differs')
                triangles=len(geometry['primitives'][0]['indices'])//3
                if lod>=0 and triangles>cfg['sourceTriangleCeilings'][lod]:
                    raise ValueError(f'{base}/{suffix} exceeds the authored source ceiling')
                points=geometry['primitives'][0]['attributes']['POSITION']
                if any(not math.isfinite(x) or x<-.50001 or x>.50001 for p in points for x in p) or abs(min(p[1] for p in points)+.5)>.00001:
                    raise ValueError('Saved geometry exceeds the normalized envelope or loses its ground anchor')
                stem=f'{layout}_{size}_{suffix}'
                glb=output/family/(stem+'.glb');write_glb(glb,geometry)
                aid=proxy_id if lod==-1 else base+('' if lod==0 else f'_lod{lod}')
                definition={'id':aid,'type':'mesh','source':f'../sources/clusters/{family}/{stem}.glb'}
                if lod>=0:
                    definition.update(materialReferences=[material_id],collisionReference=proxy_id)
                if lod==0: definition['lods']=[{'level':i,'assetId':base+f'_lod{i}','maxDistance':260 if i==1 else 520} for i in (1,2)]
                write_json(definitions/f'{family}_{stem}.asset.json',definition)
                row={'id':aid,'family':family,'layout':layout,'size':size,'lod':lod,'triangles':triangles,
                     'bounds':[[min(p[k] for p in points) for k in range(3)],[max(p[k] for p in points) for k in range(3)]],
                     'source':glb.relative_to(source_root).as_posix(),'sha256':sha(glb)}
                rows.append(row)
                if size=='medium' and layout==cfg['families'][family]['defaultLayout'] and lod>=0:
                    alias=dict(definition,id=root_id+('' if lod==0 else f'_lod{lod}'))
                    if lod==0: alias['lods']=[{'level':i,'assetId':root_id+f'_lod{i}','maxDistance':260 if i==1 else 520} for i in (1,2)]
                    write_json(definitions/(family+('' if lod==0 else f'_lod{lod}')+'.asset.json'),alias)
    return rows


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--action',choices=('Author','Export'),required=True)
    parser.add_argument('--family',choices=('all',),default='all')
    parser.add_argument('--output-root',type=Path)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]);cfg=contract()
    if bpy.app.version!=(5,2,2): raise ValueError('Use the pinned Blender 5.2.2 toolchain')
    source_root=SOURCE
    if args.action=='Export':
        if args.output_root is None: raise ValueError('Saved-master Export requires a fresh --output-root beneath repository artifacts')
        destination=(ROOT/args.output_root).resolve()
        if not destination.is_relative_to((ROOT/'artifacts').resolve()) or destination.exists():
            raise ValueError('Export destination must be fresh and strictly beneath repository artifacts')
        source_root=destination/'source'
    elif args.output_root is not None:
        raise ValueError('Author writes the canonical initial sources; --output-root belongs to Export')
    names=list(cfg['families']) if args.family=='all' else [args.family]
    rows=[];masters=[]
    for family in names:
        if family not in cfg['families']: raise ValueError('Unknown resource family')
        if args.action=='Author': author(family,cfg)
        else: bpy.ops.wm.open_mainfile(filepath=str(MASTERS/f'{family}.blend'))
        # Export uses actual editable scene vertices, including saved edits; recipes are not rerun.
        rows.extend(export(family,cfg,source_root))
        masters.append({'family':family,'path':f'world/authoring/resource_clusters/{family}.blend','sha256':sha(MASTERS/f'{family}.blend')})
    record={'schemaVersion':1,'status':'authored-not-validated','blenderVersion':'5.2.2','families':names,
            'masterSources':masters,'exports':rows,'validation':'NOT EXECUTED','humanArtApproval':'pending'}
    if args.family!='all': raise ValueError('Use all families when publishing the complete source manifest')
    target=source_root/'world/authoring/resource_clusters';target.mkdir(parents=True,exist_ok=True)
    if source_root!=SOURCE:
        shutil.copyfile(MASTERS/'variants.json',target/'variants.json')
        for family in names: shutil.copyfile(MASTERS/f'{family}.blend',target/f'{family}.blend')
        # Copy only the existing shared material/texture closure, unchanged, for optional manual compilation.
        from asset_contract import Catalog
        catalog=Catalog(SOURCE)
        closure=catalog.closure([f'material.world.resource.{family}' for family in names])
        for path in catalog.files(closure):
            relative=path.relative_to(SOURCE);copy=source_root/relative
            copy.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(path,copy)
    write_json(target/'sources.json',record)
    print(f'Authored/exported {len(rows)} mineral geometry sources; validation was not executed')


if __name__=='__main__': main()
