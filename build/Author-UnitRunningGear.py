"""Replace rubber box components with running gear and hot barrels with tubes.
Run after Refine-ProductionMeshes.py. Outer bounds and weapon sockets are fixed.
"""
import base64
import json
from pathlib import Path
from importlib.machinery import SourceFileLoader
import numpy as np

props=SourceFileLoader('props',str(Path(__file__).with_name('Author-IndustrialProps.py'))).load_module()
ROOT=props.ROOT


def wheel(mesh,x0,x1,center_z,radius):
    part=props.Mesh(); part.tube(x0,x1,radius,0,2,sides=16)
    offset=len(mesh.p)
    mesh.p.extend([np.array([q[1],q[0],q[2]+center_z]) for q in part.p])
    mesh.n.extend([np.array([q[1],q[0],q[2]]) for q in part.n])
    mesh.uv.extend(part.uv)
    # Swapping X/Y reflects orientation: reverse each triangle's winding.
    for k in range(0,len(part.idx),3):
        mesh.idx.extend([part.idx[k]+offset,part.idx[k+2]+offset,part.idx[k+1]+offset])


for meta in (ROOT/'assets/source/units/directorate').rglob('*.asset.json'):
    d=json.loads(meta.read_text())
    if d['type']!='mesh' or not d['source'].endswith('_production.gltf'): continue
    path=(meta.parent/d['source']).resolve()
    oldpath=path.with_name(path.stem.removesuffix('_production')+'.gltf')
    old=json.loads(oldpath.read_text()); g=json.loads(path.read_text())
    raw=base64.b64decode(old['buffers'][0]['uri'].split(',')[1]); p=old['meshes'][0]['primitives'][0]
    op=props.reader.read(old,raw,p['attributes']['POSITION']); ou=props.reader.read(old,raw,p['attributes']['TEXCOORD_0'])
    raw=base64.b64decode(g['buffers'][0]['uri'].split(',')[1]); p=g['meshes'][0]['primitives'][0]
    positions=props.reader.read(g,raw,p['attributes']['POSITION']); normals=props.reader.read(g,raw,p['attributes']['NORMAL']); uvs=props.reader.read(g,raw,p['attributes']['TEXCOORD_0'])
    indices=props.reader.read(g,raw,p['indices']).ravel()
    if len(positions)!=len(op)*4: raise ValueError('Run the bevel authoring pass before running gear.')
    pieces=[[],[],[],[]]; offset=0; replacements=0
    for component in range(len(op)//24):
        oldp=op[component*24:component*24+24]; lo=oldp.min(0); hi=oldp.max(0); extent=hi-lo
        uvcenter=ou[component*24:component*24+24].mean(0)
        cell=int(uvcenter[1]*4)*4+int(uvcenter[0]*4)
        mesh=None
        if cell==2:
            mesh=props.Mesh()
            if extent[2]>extent[1]*2:
                # Continuous outer belt with visible inner road wheels.
                mesh.box([-1,.78,-1],[1,1,1],2); mesh.box([-1,-1,-1],[1,-.78,1],2)
                mesh.box([-1,-.78,-1],[1,.78,-.85],2); mesh.box([-1,-.78,.85],[1,.78,1],2)
                for z in np.linspace(-.64,.64,4): wheel(mesh,-.98,.98,z,.28)
            else: wheel(mesh,-1,1,0,1)
        elif cell==7 and extent[2]>max(extent[0],extent[1])*2:
            mesh=props.Mesh(); mesh.tube(-1,1,1,.58,7,sides=16)
            mesh.p=[np.array([q[0],-q[2],q[1]]) for q in mesh.p]
            mesh.n=[np.array([q[0],-q[2],q[1]]) for q in mesh.n]
        if mesh is not None:
            vp=np.asarray(mesh.p); scale=extent/(vp.max(0)-vp.min(0))
            vp=lo+(vp-vp.min(0))*scale; vn=np.asarray(mesh.n)/scale; vn/=np.linalg.norm(vn,axis=1)[:,None]
            vu=np.asarray(mesh.uv); vi=np.asarray(mesh.idx,dtype=np.uint32)
            replacements+=1
        else:
            vp=positions[component*96:component*96+96]; vn=normals[component*96:component*96+96]; vu=uvs[component*96:component*96+96]
            vi=indices[component*132:component*132+132]-component*96
        pieces[0].extend(vp); pieces[1].extend(vn); pieces[2].extend(vu); pieces[3].extend(vi+offset); offset+=len(vp)
    if not replacements: continue
    arrays=[np.asarray(a,dtype='<u4' if i==3 else '<f4') for i,a in enumerate(pieces)]
    assert np.allclose(arrays[0].min(0),op.min(0),atol=1e-6)
    assert np.allclose(arrays[0].max(0),op.max(0),atol=1e-6)
    blob=b''; views=[]; acc=[]
    for a,typ,comp in zip(arrays,('VEC3','VEC3','VEC2','SCALAR'),(5126,5126,5126,5125)):
        payload=a.tobytes(); views.append(dict(buffer=0,byteOffset=len(blob),byteLength=len(payload))); acc.append(dict(bufferView=len(views)-1,componentType=comp,count=len(a),type=typ)); blob+=payload
    g['buffers']=[dict(byteLength=len(blob),uri='data:application/octet-stream;base64,'+base64.b64encode(blob).decode())]; g['bufferViews']=views; g['accessors']=acc
    path.write_text(json.dumps(g,indent=2)+'\n')
    print(d['id'],replacements,'running gear/barrel components',len(arrays[3])//3,'triangles')
