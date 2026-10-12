"""Export editable operational building components and bounded rigid ranges.

The JSON recipe owns named parts, construction/damage membership and LODs.
Exports retain one material primitive; contiguous component ranges describe the
source hierarchy without introducing runtime scene nodes or resource owners.
"""
import argparse
import hashlib
import json
from importlib.machinery import SourceFileLoader
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source/buildings/directorate'
Mesh = SourceFileLoader('operational_primitives', str(ROOT/'build/Author-CommandCore.py')).load_module().Mesh
STATES = ('foundation','structural_frame','partial_shell','unpowered','damaged','critical','destroyed')

def emit(mesh, part, lod):
    kind, cell = part['kind'], part['cell']
    if kind == 'box':
        mesh.box(part['min'],part['max'],cell,part.get('bevel',0) if lod==0 else 0)
    elif kind == 'cylinder':
        mesh.cylinder(part['start'],part['end'],part['radius'],cell,part['sides'][lod],part.get('inner',0))
    elif kind == 'dish':
        mesh.dish(part['centre'],part['radius'],cell,part['sides'][lod],part['rings'][lod])
        # Close the double-sheet reflector rim; no exposed open edge.
        centre=np.array(part['centre']); radius=part['radius']; n=np.array([0,.8,.6])
        def rim(k):
            a=2*np.pi*k/part['sides'][lod]
            return centre+radius*(np.array([1,0,0])*np.cos(a)+np.array([0,.6,-.8])*np.sin(a))+.45*radius*n
        for k in range(part['sides'][lod]):
            mesh.face([rim(k),rim(k)-.004*n,rim(k+1)-.004*n,rim(k+1)],1,rim(k)-centre)
    elif kind == 'gable':
        x0,x1=part['width'];z0,z1=part['depth'];base,eave,ridge=part['heights'];x=(x0+x1)/2
        vertices=np.array([[x0,base,z0],[x1,base,z0],[x1,base,z1],[x0,base,z1],
                           [x0,eave,z0],[x1,eave,z0],[x1,eave,z1],[x0,eave,z1],[x,ridge,z0],[x,ridge,z1]])
        for ids,n in (([0,3,2,1],[0,-1,0]),([0,1,5,8,4],[0,0,-1]),([3,7,9,6,2],[0,0,1]),
                      ([0,4,7,3],[-1,0,0]),([1,2,6,5],[1,0,0]),([4,8,9,7],[-1,1,0]),([8,5,6,9],[1,1,0])):
            mesh.face(vertices[ids],cell,n)
    elif kind == 'radial_blade':
        temporary=Mesh();r0,r1=part['inner'],part['outer'];w=part['width']/2;t=part['thickness']/2
        temporary.box([r0,-t,-w],[r1,t,w],cell)
        angle=np.deg2rad(part['angle']);rotation=np.array([[np.cos(angle),0,np.sin(angle)],[0,1,0],[-np.sin(angle),0,np.cos(angle)]])
        first=len(mesh.positions)
        mesh.positions.extend(np.array(temporary.positions)@rotation.T+np.array(part['centre']))
        mesh.normals.extend(np.array(temporary.normals)@rotation.T);mesh.uvs.extend(temporary.uvs)
        mesh.indices.extend(first+i for i in temporary.indices)
    else:
        raise ValueError(f'Unsupported component: {kind}')

def assemble(family,lod,state):
    mesh=Mesh(); hierarchy=[]; groups=[]
    names=['body']+list(family['motion']) if state=='intact' and lod==0 else ['static']
    for group in names:
        first=len(mesh.indices)
        for module in family['modules']:
            if state not in module['states'] or (group!='static' and module['group']!=group):
                continue
            for part in module['parts']:
                if lod not in part['lods']:continue
                start=len(mesh.indices);emit(mesh,part,lod)
                hierarchy.append(dict(module=module['name'],part=part['name'],group=group,firstIndex=start,indexCount=len(mesh.indices)-start))
        count=len(mesh.indices)-first
        if count:groups.append(dict(name=group,firstIndex=first,indexCount=count,parent=-1))
    return mesh,hierarchy,groups

def validate(mesh):
    p=np.asarray(mesh.positions);n=np.asarray(mesh.normals);uv=np.asarray(mesh.uvs);idx=np.asarray(mesh.indices).reshape(-1,3)
    if not all(np.isfinite(a).all() for a in (p,n,uv)):raise ValueError('Non-finite source channel')
    cross=np.cross(p[idx[:,1]]-p[idx[:,0]],p[idx[:,2]]-p[idx[:,0]])
    if np.min(np.linalg.norm(cross,axis=1))<1e-10:raise ValueError('Degenerate source triangle')
    if not np.allclose(np.linalg.norm(n,axis=1),1,atol=1e-5):raise ValueError('Non-unit normal')
    if np.min(np.einsum('ij,ij->i',cross,n[idx[:,0]]))<=0:raise ValueError('Reversed source winding')
    if uv.min()<0 or uv.max()>1:raise ValueError('Unbounded atlas coordinates')

def author(recipe,output,update):
    kit=json.loads(recipe.read_text(encoding='utf-8-sig'));report={}
    for name,family in kit['families'].items():
        for state,lod in [('intact',i) for i in range(3)]+[(s,1) for s in STATES]:
            mesh,hierarchy,groups=assemble(family,lod,state);validate(mesh)
            suffix=f'lod{lod}' if state=='intact' else state
            path=output/f'{name}_{suffix}.gltf';entry=mesh.export(path)
            gltf=json.loads(path.read_text(encoding='utf-8'));gltf['asset']['generator']='ForgeLine operational building component exporter'
            gltf['extras']=dict(componentHierarchy=hierarchy,axes=kit['axes'],state=state,lod=lod)
            path.write_text(json.dumps(gltf,indent=2)+'\n',encoding='utf-8')
            entry['sha256']=hashlib.sha256(path.read_bytes()).hexdigest();entry['groups']=groups
            report[f'{name}.{suffix}']=entry
            if update:
                meta=SOURCE/(name+(f'_lod{lod}' if state=='intact' and lod else '_'+state if state!='intact' else '')+'.asset.json')
                d=json.loads(meta.read_text(encoding='utf-8-sig')) if meta.exists() else dict(id='building.directorate.'+name+'.'+state,type='mesh')
                d['source']=path.relative_to(SOURCE).as_posix()
                d['materialReferences']=['material.directorate.building.'+(state if state in ('unpowered','damaged','critical','destroyed') else 'structural')]
                d.pop('mechanicalParts',None)
                if state=='intact' and lod==0 and family['motion']:
                    d['sockets']=[dict(name=m['socket'],x=m['pivot'][0],y=m['pivot'][1],z=m['pivot'][2]) for m in family['motion'].values()]
                    for group in groups:
                        motion=family['motion'].get(group['name'])
                        group.update({k:motion[k] for k in ('motion','socket','rate','travel')} if motion else dict(motion='static'))
                    d['mechanicalParts']=groups
                meta.write_text(json.dumps(d,indent=2)+'\n',encoding='utf-8')
    (output/'operational.exports.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({name:entry['triangles'] for name,entry in report.items()},indent=2))

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--recipe',type=Path,default=SOURCE/'authoring/operational_buildings.kit.json')
    parser.add_argument('--output',type=Path,default=SOURCE/'sources/operational')
    parser.add_argument('--update-metadata',action='store_true')
    args=parser.parse_args();author(args.recipe,args.output,args.update_metadata)
