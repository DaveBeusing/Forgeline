"""Author functional prop silhouettes in the existing bounds and UV atlas.
Requires numpy. Sources, reduced LODs and collision contracts are retained.
"""
import base64
import json
from pathlib import Path
import numpy as np
from importlib.machinery import SourceFileLoader

ROOT=Path(__file__).resolve().parents[1]
reader=SourceFileLoader('mesh_reader',str(Path(__file__).with_name('Refine-ProductionMeshes.py'))).load_module()


class Mesh:
    def __init__(self): self.p=[]; self.n=[]; self.uv=[]; self.idx=[]
    def polygon(self,points,cell):
        q=np.asarray(points,dtype=float)
        n=np.cross(q[1]-q[0],q[2]-q[0]); n/=np.linalg.norm(n)
        start=len(self.p); self.p.extend(q); self.n.extend([n]*len(q))
        r,c=divmod(cell,4)
        axes=[i for i in range(3) if i!=int(np.argmax(np.abs(n)))]
        for point in q:
            local=np.clip((point[axes]+1)*.5,0,1)
            self.uv.append(np.array([c,r])*.25+.018+local*.214)
        for j in range(1,len(q)-1): self.idx.extend((start,start+j,start+j+1))
    def box(self,lo,hi,cell):
        lo=np.array(lo); hi=np.array(hi)
        for axis in range(3):
            a,b=[i for i in range(3) if i!=axis]
            for sign in (-1,1):
                pts=[]
                for sa,sb in ((0,0),(1,0),(1,1),(0,1)):
                    q=lo.copy(); q[axis]=lo[axis] if sign<0 else hi[axis]; q[a]=hi[a] if sa else lo[a]; q[b]=hi[b] if sb else lo[b]; pts.append(q)
                if np.cross(pts[1]-pts[0],pts[2]-pts[0])[axis]*sign<0: pts.reverse()
                self.polygon(pts,cell)
    def tube(self,y0,y1,radius,inner,cell,sides=24):
        ring=lambda r,y,k: [r*np.cos(k*2*np.pi/sides),y,r*np.sin(k*2*np.pi/sides)]
        for k in range(sides):
            j=k+1
            self.polygon([ring(radius,y0,k),ring(radius,y1,k),ring(radius,y1,j),ring(radius,y0,j)],cell)
            if inner:
                self.polygon([ring(inner,y0,j),ring(inner,y1,j),ring(inner,y1,k),ring(inner,y0,k)],cell)
                self.polygon([ring(inner,y1,k),ring(inner,y1,j),ring(radius,y1,j),ring(radius,y1,k)],cell)
                self.polygon([ring(inner,y0,j),ring(inner,y0,k),ring(radius,y0,k),ring(radius,y0,j)],cell)
            else:
                self.polygon([[0,y1,0],ring(radius,y1,j),ring(radius,y1,k)],cell)
                self.polygon([[0,y0,0],ring(radius,y0,k),ring(radius,y0,j)],cell)
    def write(self,path,lo,hi):
        pos=np.asarray(self.p); mn=pos.min(0); mx=pos.max(0)
        scale=(hi-lo)/(mx-mn); pos=lo+(pos-mn)*scale
        normals=np.asarray(self.n)/scale; normals/=np.linalg.norm(normals,axis=1)[:,None]
        arrays=[pos.astype('<f4'),normals.astype('<f4'),np.asarray(self.uv,dtype='<f4'),np.asarray(self.idx,dtype='<u4')]
        blob=b''; views=[]; acc=[]
        for a,typ,comp in zip(arrays,('VEC3','VEC3','VEC2','SCALAR'),(5126,5126,5126,5125)):
            b=a.tobytes(); views.append(dict(buffer=0,byteOffset=len(blob),byteLength=len(b))); acc.append(dict(bufferView=len(views)-1,componentType=comp,count=len(a),type=typ)); blob+=b
        g=dict(asset=dict(version='2.0',generator='ForgeLine industrial prop authoring'),buffers=[dict(byteLength=len(blob),uri='data:application/octet-stream;base64,'+base64.b64encode(blob).decode())],bufferViews=views,accessors=acc,meshes=[dict(primitives=[dict(attributes=dict(POSITION=0,NORMAL=1,TEXCOORD_0=2),indices=3,mode=4)])])
        path.write_text(json.dumps(g,indent=2)+'\n')


def author_props():
    for name in ('drum','pipe_section','pallet','fence','industrial_light_signage','rock','rubble'):
        meta=ROOT/f'assets/source/world/meshes/props/{name}.asset.json'
        d=json.loads(meta.read_text()); path=(meta.parent/d['source']).resolve()
        original=path.with_name(path.stem.removesuffix('_production')+'.gltf')
        g=json.loads(original.read_text()); raw=base64.b64decode(g['buffers'][0]['uri'].split(',')[1]); p=g['meshes'][0]['primitives'][0]
        pos=reader.read(g,raw,p['attributes']['POSITION']); lo=pos.min(0); hi=pos.max(0)
        m=Mesh()
        if name=='drum':
            m.tube(-1,1,.84,0,0)
            for y in (-.96,-.35,.35,.96): m.tube(y-.035,y+.035,.89,.82,1)
            m.box([.2,.98,-.12],[.4,1.04,.08],1)
        elif name=='pipe_section':
            m.tube(-1,1,.82,.63,1)
            for y in (-.94,.84): m.tube(y,y+.10,1,.63,7)
            # Lay the hollow bore horizontally along the existing source Z axis.
            m.p=[np.array([q[0],q[2],-q[1]]) for q in m.p]
            m.n=[np.array([q[0],q[2],-q[1]]) for q in m.n]
        elif name=='pallet':
            for x in (-.8,0,.8):
                for z in (-.8,0,.8): m.box([x-.18,-.6,z-.18],[x+.18,.25,z+.18],10)
            for z in np.linspace(-.88,.88,7): m.box([-1,.3,z-.11],[1,.5,z+.11],10)
            for x in (-.8,0,.8): m.box([x-.18,-.8,-1],[x+.18,-.6,1],10)
        elif name=='fence':
            for x in (-.95,.95): m.box([x-.05,-1,-.08],[x+.05,1,.08],1)
            for y in (-.75,.75): m.box([-1,y-.035,-.04],[1,y+.035,.04],1)
            for x in np.linspace(-.8,.8,11): m.box([x-.012,-.8,-.02],[x+.012,.8,.02],1)
        elif name=='industrial_light_signage':
            m.box([-.06,-1,-.06],[.06,.75,.06],1)
            m.box([-.65,.65,-.22],[.65,1,.22],15)
            m.box([-.6,.72,.22],[.6,.94,.25],4)
            m.box([-.25,-1,-.25],[.25,-.9,.25],6)
        else:
            rng=np.random.default_rng(144 if name=='rock' else 188)
            for component in range(1 if name=='rock' else 5):
                center=np.zeros(3) if name=='rock' else rng.uniform(-.65,.65,3)
                radius=1 if name=='rock' else rng.uniform(.25,.5)
                equator=[center+np.array([np.cos(k*np.pi/4),rng.uniform(-.25,.15),np.sin(k*np.pi/4)])*radius*rng.uniform(.8,1.05) for k in range(8)]
                top=center+np.array([.14,.85,-.08])*radius; bottom=center+np.array([-.1,-.65,.05])*radius
                for k in range(8):
                    j=(k+1)%8
                    m.polygon([equator[k],top,equator[j]],11)
                    m.polygon([equator[j],bottom,equator[k]],11)
        dest=original.with_name(original.stem+'_production.gltf'); m.write(dest,lo,hi)
        d['source']=str(Path(d['source']).with_name(dest.name)).replace('\\','/')
        meta.write_text(json.dumps(d,indent=2)+'\n')
        print(name,len(m.idx)//3,'triangles')


if __name__ == '__main__': author_props()
