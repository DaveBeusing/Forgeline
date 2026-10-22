"""Generate genuine closed Blender ribbons for the two disconnected legacy curves."""
import copy
import json
import sys
from collections import Counter
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from asset_contract import read_geometry,geometry_stats,game_to_blender,blender_to_game,write_glb,write_json,require,sha
from infrastructure_contract import family_catalog,export_filename
from infrastructure_curves_contract import centreline,outline


def run(request):
    output=Path(request['outputRoot']);fixture=json.loads(Path(request['arguments'][0]).read_text(encoding='utf-8-sig'))
    catalog=family_catalog(request['inputRoot'],fixture);report=dict(success=False,meshes=[])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        for suffix in ('curve_short','curve_long'):
            aid='infrastructure.directorate.road.'+suffix
            baseline=read_geometry(catalog.source(aid),catalog.root,catalog.definitions[aid][1]);stats=geometry_stats(baseline)
            path=centreline(aid,stats);ring=outline(path);n=len(ring)
            points=[(x,stats['boundsMax'][1],z) for x,z in ring]+[(x,stats['boundsMin'][1],z) for x,z in ring]
            faces=[tuple(range(n)),tuple(reversed(range(n,2*n)))]+[(n+i,n+(i+1)%n,(i+1)%n,i) for i in range(n)]
            mesh=bpy.data.meshes.new(suffix);mesh.from_pydata([game_to_blender(p) for p in points],[],faces);mesh.update()
            require(all(v==2 for v in Counter(tuple(sorted(edge)) for face in faces for edge in zip(face,face[1:]+face[:1])).values()),'Curve must be a closed manifold slab')
            mesh.calc_loop_triangles()
            old=baseline['primitives'][0];uvs=old['attributes']['TEXCOORD_0']
            low=[min(v[k] for v in uvs) for k in (0,1)];high=[max(v[k] for v in uvs) for k in (0,1)]
            require(all(0<high[k]-low[k]<=.250001 for k in (0,1)),'Curve must retain its single atlas cell')
            attrs={'POSITION':[],'NORMAL':[],'TEXCOORD_0':[]};indices=[];vertices={}
            for tri in mesh.loop_triangles:
                for loop_index in tri.loops:
                    loop=mesh.loops[loop_index]
                    position=tuple(blender_to_game(mesh.vertices[loop.vertex_index].co))
                    normal=tuple(blender_to_game(mesh.corner_normals[loop_index].vector))
                    if abs(normal[1])>.5:axes=(0,2)
                    else:axes=(2 if abs(normal[0])>abs(normal[2]) else 0,1)
                    uv=tuple(low[k]+(position[axis]-stats['boundsMin'][axis])/(stats['boundsMax'][axis]-stats['boundsMin'][axis])*(high[k]-low[k]) for k,axis in enumerate(axes))
                    key=(position,normal,uv)
                    if key not in vertices:
                        vertices[key]=len(attrs['POSITION'])
                        for semantic,value in zip(attrs,key):attrs[semantic].append(value)
                    indices.append(vertices[key])
            result=copy.deepcopy(baseline);result['primitives'][0]['attributes']=attrs;result['primitives'][0]['indices']=indices
            after=geometry_stats(result)
            maximum=max(abs(a-b) for key in ('boundsMin','boundsMax') for a,b in zip(stats[key],after[key]))
            require(maximum<=1e-6,'Curve envelope changed')
            require(after['triangles']<=stats['triangles'] and after['vertices']<=stats['vertices'],'Curve exceeds existing geometry budget')
            target=output/export_filename(aid);write_glb(target,result)
            report['meshes'].append(dict(id=aid,originalSha256=sha(catalog.source(aid)),exportSha256=sha(target),before=stats,after=after,centreline=path,maximumBoundsError=maximum,closedManifold=True,atlasRectangle=[low,high]))
        report['success']=True
    finally:write_json(output/'curve-report.json',report)


if __name__=='__main__':run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
