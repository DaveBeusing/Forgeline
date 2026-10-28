"""Save and reopen editable variant rest meshes in pinned Blender."""
import json
import argparse
import sys
from pathlib import Path
import bpy

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/blender'))
from asset_contract import read_geometry, game_to_blender, blender_to_game, require, write_json, write_glb, sha


def export_saved(master, output):
    source=ROOT/'assets/source'
    require(not output.resolve().is_relative_to(source.resolve()),'Export edits to an isolated review directory')
    bpy.ops.wm.open_mainfile(filepath=str(master))
    require(len(bpy.context.scene.objects)==2,'Expected exactly High/Reduced rest objects')
    for obj in bpy.context.scene.objects:
        aid=obj.get('stable_id')
        name=aid.split('.')[-1]
        p=source/'world/meshes/variants'/f'{name}.asset.json'
        d=json.loads(p.read_text())
        require(d['id']==aid and obj.type=='MESH' and not obj.modifiers and not obj.animation_data,
                'Unsupported rest object edit')
        require(obj.matrix_world.is_identity,'Bake no object transforms into this source contract')
        require(obj.get('material_id')==d['materialReferences'][0],'Material identity changed')
        g=read_geometry(p.parent/d['source'],source,d)
        primitive=g['primitives'][0]
        indices=[v for polygon in obj.data.polygons for v in polygon.vertices]
        require(all(len(p.vertices)==3 for p in obj.data.polygons) and indices==primitive['indices'],
                'Keep the qualified topology and LOD budget')
        require(len(obj.data.vertices)==len(primitive['attributes']['POSITION']),'Vertex count changed')
        attributes=primitive['attributes']
        attributes['POSITION']=[blender_to_game(v.co) for v in obj.data.vertices]
        normals=[None]*len(obj.data.vertices)
        uvs=[None]*len(obj.data.vertices)
        for loop in obj.data.loops:
            normal=blender_to_game(obj.data.corner_normals[loop.index].vector)
            uv=tuple(obj.data.uv_layers[0].data[loop.index].uv)
            i=loop.vertex_index
            require(uvs[i] is None or uvs[i]==uv,'Split vertices at authored UV seams')
            require(normals[i] is None or max(abs(a-b) for a,b in zip(normals[i],normal))<1e-5,
                    'Split vertices at authored normal seams')
            normals[i],uvs[i]=normal,uv
        require(all(n is not None for n in normals),'Unreferenced source vertices')
        attributes['NORMAL'],attributes['TEXCOORD_0']=normals,uvs
        write_glb(output/f'{name}.glb',g)


def main():
    require(bpy.app.version[:3]==(5,2,2),'Expected pinned Blender 5.2.2')
    source=ROOT/'assets/source'
    output=ROOT/'assets/staged/environment-variants'
    recipe=json.loads(Path(__file__).with_name('variants.json').read_text())
    reports=[]
    for row in recipe['variants']:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        expected={}
        for suffix in ('','_lod1'):
            name=row['id'].split('.')[-1]+suffix
            p=source/'world/meshes/variants'/f'{name}.asset.json'
            d=json.loads(p.read_text())
            g=read_geometry(p.parent/d['source'],source,d)
            require(len(g['primitives'])==1,'Expected one shared material section')
            primitive=g['primitives'][0]
            attrs=primitive['attributes']
            mesh=bpy.data.meshes.new(name)
            indices=primitive['indices']
            mesh.from_pydata([game_to_blender(v) for v in attrs['POSITION']],[],
                             [indices[i:i+3] for i in range(0,len(indices),3)])
            mesh.update()
            uv=mesh.uv_layers.new(name='UVMap')
            for loop in mesh.loops:uv.data[loop.index].uv=attrs['TEXCOORD_0'][loop.vertex_index]
            mesh.normals_split_custom_set_from_vertices([game_to_blender(v) for v in attrs['NORMAL']])
            obj=bpy.data.objects.new(name,mesh)
            bpy.context.collection.objects.link(obj)
            obj['stable_id']=d['id']
            obj['family_id']=row['familyId']
            obj['material_id']=d['materialReferences'][0]
            obj['source_sha256']=sha(p.parent/d['source'])
            obj.hide_render=bool(suffix)
            expected[name]=attrs
        bpy.context.scene.unit_settings.system='METRIC'
        bpy.context.scene.unit_settings.scale_length=1
        target=output/(row['id'].split('.')[-1]+'.blend')
        target.parent.mkdir(parents=True,exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(target),check_existing=False)
        bpy.ops.wm.open_mainfile(filepath=str(target))
        for name,attrs in expected.items():
            obj=bpy.data.objects[name]
            actual=[blender_to_game(v.co) for v in obj.data.vertices]
            deviation=max(abs(a-b) for p,q in zip(actual,attrs['POSITION'],strict=True) for a,b in zip(p,q,strict=True))
            require(deviation<=5e-8,'Saved master moved geometry')
            require(len(obj.data.polygons)*3==len(obj.data.loops),'Unexpected triangulation')
            require(len(obj.data.uv_layers)==1,'UV0 missing')
            for loop in obj.data.loops:
                require(max(abs(a-b) for a,b in zip(obj.data.uv_layers[0].data[loop.index].uv,attrs['TEXCOORD_0'][loop.vertex_index]))<=5e-8,'Saved UV changed')
            reports.append(dict(id=obj['stable_id'],sourceSha256=obj['source_sha256'],
                                maximumPositionDeviation=deviation,vertices=len(actual),triangles=len(obj.data.polygons)))
    write_json(output/'native-readback.json',dict(schemaVersion=1,blenderVersion=bpy.app.version_string,assets=reports))


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--export-saved',type=Path)
    parser.add_argument('--output',type=Path)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.export_saved:
        require(args.output is not None,'An isolated output directory is required')
        export_saved(args.export_saved,args.output)
    else:main()
