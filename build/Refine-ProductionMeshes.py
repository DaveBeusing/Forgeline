"""Author small manufactured edge bevels on existing box component meshes.

Retains the original bounds, face UV regions, component positions and asset
contracts. Writes sibling production sources, never collision or reduced LODs.
Requires numpy. Unsupported topology is reported and left intact.
"""
from pathlib import Path
import base64
import itertools
import json
import numpy as np

ROOT=Path(__file__).resolve().parents[1]


def read(g, raw, index):
    a=g['accessors'][index]; v=g['bufferViews'][a['bufferView']]
    count={'VEC3':3,'VEC2':2,'SCALAR':1}[a['type']]
    dtype='<f4' if a['componentType']==5126 else '<u4'
    return np.frombuffer(raw,dtype=dtype,count=a['count']*count,offset=v.get('byteOffset',0)+a.get('byteOffset',0)).reshape(-1,count)


def refine(path):
    g=json.loads(path.read_text()); primitives=g['meshes'][0]['primitives']
    if len(primitives)!=1: return None
    p=primitives[0]; attrs=p['attributes']
    if not all(a in attrs for a in ('POSITION','NORMAL','TEXCOORD_0')): return None
    raw=base64.b64decode(g['buffers'][0]['uri'].split(',')[1])
    pos=read(g,raw,attrs['POSITION']); nor=read(g,raw,attrs['NORMAL']); uv=read(g,raw,attrs['TEXCOORD_0'])
    idx=read(g,raw,p['indices']).ravel()
    if len(pos)%24 or len(idx)!=len(pos)//24*36: return None
    vertices=[]; normals=[]; texcoords=[]; indices=[]
    for start in range(0,len(pos),24):
        old=pos[start:start+24]; lo=old.min(0); hi=old.max(0)
        if len(np.unique(old,axis=0))!=8 or np.any(hi-lo<1e-5): return None
        if not np.all(np.isclose(old,lo,atol=1e-6)|np.isclose(old,hi,atol=1e-6)): return None
        center=(lo+hi)/2; half=(hi-lo)/2; b=float((hi-lo).min()*.035)
        faces={}
        for f in range(6):
            n=nor[start+f*4:start+f*4+4].mean(0)
            axis=int(np.argmax(np.abs(n))); sign=1 if n[axis]>0 else -1
            faces[(axis,sign)]=(old[f*4:f*4+4],uv[start+f*4:start+f*4+4])
        if len(faces)!=6: return None
        def face_uv(point,n):
            axis=int(np.argmax(np.abs(n))); sign=1 if n[axis]>0 else -1
            oldp,oldu=faces[(axis,sign)]
            # Affine fit reproduces original face orientation and atlas cell.
            axes=[a for a in range(3) if a!=axis]
            design=np.column_stack((oldp[:,axes],np.ones(4)))
            coeff=np.linalg.lstsq(design,oldu,rcond=None)[0]
            return np.r_[point[axes],1]@coeff
        def polygon(points,n):
            n=np.asarray(n,dtype=float); n/=np.linalg.norm(n)
            points=[center+np.array(q) for q in points]
            if np.dot(np.cross(points[1]-points[0],points[2]-points[0]),n)<0: points.reverse()
            offset=len(vertices)
            for q in points:
                vertices.append(q); normals.append(n); texcoords.append(face_uv(q,n))
            for j in range(1,len(points)-1): indices.extend((offset,offset+j,offset+j+1))
        for axis in range(3):
            a,c=[j for j in range(3) if j!=axis]
            for s in (-1,1):
                points=[]
                for sa,sc in ((-1,-1),(1,-1),(1,1),(-1,1)):
                    q=np.zeros(3); q[axis]=s*half[axis]; q[a]=sa*(half[a]-b); q[c]=sc*(half[c]-b); points.append(q)
                n=np.zeros(3); n[axis]=s; polygon(points,n)
        for a,c in itertools.combinations(range(3),2):
            axis=next(j for j in range(3) if j not in (a,c))
            for sa,sc in itertools.product((-1,1),repeat=2):
                points=[]
                for endpoint,face in ((-1,0),(1,0),(1,1),(-1,1)):
                    q=np.zeros(3); q[axis]=endpoint*(half[axis]-b)
                    q[a]=sa*(half[a]-(b if face else 0)); q[c]=sc*(half[c]-(0 if face else b)); points.append(q)
                n=np.zeros(3); n[a]=sa; n[c]=sc; polygon(points,n)
        for signs in itertools.product((-1,1),repeat=3):
            points=[]
            for axis in range(3):
                q=np.array(signs)*(half-b); q[axis]=signs[axis]*half[axis]; points.append(q)
            polygon(points,signs)
    arrays=[np.asarray(vertices,dtype='<f4'),np.asarray(normals,dtype='<f4'),np.asarray(texcoords,dtype='<f4'),np.asarray(indices,dtype='<u4')]
    data=b''; views=[]; accessors=[]
    for array,typ,comp in zip(arrays,('VEC3','VEC3','VEC2','SCALAR'),(5126,5126,5126,5125)):
        payload=array.tobytes(); views.append(dict(buffer=0,byteOffset=len(data),byteLength=len(payload)))
        accessors.append(dict(bufferView=len(views)-1,componentType=comp,count=len(array),type=typ)); data+=payload
    out=dict(asset=dict(version='2.0',generator='ForgeLine manufactured surface authoring'),buffers=[dict(byteLength=len(data),uri='data:application/octet-stream;base64,'+base64.b64encode(data).decode())],bufferViews=views,accessors=accessors,meshes=[dict(primitives=[dict(attributes=dict(POSITION=0,NORMAL=1,TEXCOORD_0=2),indices=3,mode=4)])])
    # Exact outer bounds retained, including components with asymmetric pivots.
    assert np.allclose(arrays[0].min(0),pos.min(0),atol=1e-6)
    assert np.allclose(arrays[0].max(0),pos.max(0),atol=1e-6)
    dest=path.with_name(path.stem+'_production.gltf')
    dest.write_text(json.dumps(out,indent=2)+'\n')
    return dest,len(idx)//3,len(indices)//3


if __name__=='__main__':
    refined={}
    for meta in (ROOT/'assets/source').rglob('*.asset.json'):
        d=json.loads(meta.read_text()); name=d['id']
        if d['type']!='mesh' or not (name.startswith('unit.directorate.') or name.startswith('building.directorate.') or name.startswith('mesh.world.prop.')): continue
        if any(s in name for s in ('.collision','.lod1','.lod2','_lod1','_lod2','.module.','.symbol.','.state.')): continue
        if not d.get('lods') and name!='unit.directorate.main_battle_tank.turret': continue
        source=(meta.parent/d['source']).resolve()
        if source.stem.endswith('_production'):
            source=source.with_name(source.stem.removesuffix('_production')+'.gltf')
        if source.suffix!='.gltf': continue
        if source not in refined: refined[source]=refine(source)
        result=refined[source]
        if result:
            d['source']=str(Path(d['source']).with_name(result[0].name)).replace('\\','/')
            meta.write_text(json.dumps(d,indent=2)+'\n')
            print(name,result[1],'->',result[2],'triangles',flush=True)
        else: print('Retained non-box topology:',name,flush=True)
