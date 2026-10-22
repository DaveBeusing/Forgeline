"""Deterministic Directorate conveyor geometry and existing rigid-clip source data.

No renderer, simulation or third-party Python dependency. All authored coordinates
are engine metres; exported meshes/sockets use one uniform 18 m source frame.
"""
from __future__ import annotations
import copy, json, math, struct
from pathlib import Path

FRAME=18.0
MATERIAL='material.infrastructure.directorate.conveyor'
STATUS={'flowing':4,'idle_no_input':12,'congested':13,'blocked':13,
        'unpowered':9,'disconnected':15,'damaged':8,'critical':11,'structural_intact':14}
ROOT=Path(__file__).resolve().parents[2]
KIT=ROOT/'assets/source/infrastructure/directorate/conveyors/conveyor-kit.v1.json'


def add(a,b): return tuple(x+y for x,y in zip(a,b))
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def mul(a,s): return tuple(x*s for x in a)
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def unit(a): return mul(a,1/math.sqrt(dot(a,a)))
def write_json(path,value):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')


class Mesh:
    def __init__(self): self.positions=[];self.normals=[];self.uvs=[];self.indices=[]
    def face(self,points,cell,outward):
        points=list(points);n=unit(cross(sub(points[1],points[0]),sub(points[2],points[0])))
        if dot(n,outward)<0: points.reverse();n=mul(n,-1)
        u=unit(sub(points[1],points[0]));v=cross(n,u)
        coords=[(dot(p,u),dot(p,v)) for p in points]
        lo=[min(p[k] for p in coords) for k in (0,1)];hi=[max(p[k] for p in coords) for k in (0,1)]
        uv=[((cell%4)*.25+.018+.214*(p[0]-lo[0])/max(hi[0]-lo[0],1e-9),
             (cell//4)*.25+.018+.214*(p[1]-lo[1])/max(hi[1]-lo[1],1e-9)) for p in coords]
        first=len(self.positions);self.positions.extend(points);self.normals.extend([n]*len(points));self.uvs.extend(uv)
        for i in range(1,len(points)-1):self.indices.extend([first,first+i,first+i+1])
    def box(self,lo,hi,cell=1):
        for axis in range(3):
            a,b=[k for k in range(3) if k!=axis]
            for side in (0,1):
                points=[]
                for sa,sb in ((0,0),(1,0),(1,1),(0,1)):
                    p=[0.,0.,0.];p[axis]=(lo,hi)[side][axis];p[a]=(lo,hi)[sa][a];p[b]=(lo,hi)[sb][b];points.append(p)
                n=[0,0,0];n[axis]=1 if side else -1;self.face(points,cell,n)
    def beam(self,a,b,width,height,cell=1):
        tangent=unit(sub(b,a));side=unit(cross((0,1,0),tangent));up=unit(cross(tangent,side))
        p=[add(add(center,mul(side,x*width/2)),mul(up,y*height/2)) for center in (a,b) for x,y in ((-1,-1),(1,-1),(1,1),(-1,1))]
        for ids,n in [((0,3,2,1),mul(tangent,-1)),((4,5,6,7),tangent),((0,1,5,4),mul(up,-1)),((3,7,6,2),up),((0,4,7,3),mul(side,-1)),((1,2,6,5),side)]: self.face([p[i] for i in ids],cell,n)
    def cylinder(self,a,b,r,cell,sides):
        axis=unit(sub(b,a));u=unit(cross(axis,(0,1,0) if abs(axis[1])<.9 else (1,0,0)));v=cross(axis,u)
        def ring(c,k):return add(c,add(mul(u,r*math.cos(k*math.tau/sides)),mul(v,r*math.sin(k*math.tau/sides))))
        for k in range(sides):self.face([ring(a,k),ring(a,k+1),ring(b,k+1),ring(b,k)],cell,sub(ring(a,k+.5),a))
        self.face([ring(a,k) for k in range(sides)],cell,mul(axis,-1));self.face([ring(b,k) for k in range(sides)],cell,axis)
    def ribbon(self,path,width,bottom,top,cell):
        rings=[]
        for i,p in enumerate(path):
            d=unit(sub(path[min(i+1,len(path)-1)],path[max(0,i-1)]))
            if i in (0,len(path)-1):d=(0,0,1 if d[2]>=0 else -1) if abs(d[2])>=abs(d[0]) else (1 if d[0]>=0 else -1,0,0)
            side=unit(cross((0,1,0),d))
            rings.append([add(add(p,mul(side,s*width/2)),(0,y,0)) for s,y in ((-1,bottom),(1,bottom),(1,top),(-1,top))])
        for a,b in zip(rings,rings[1:]):
            for k,n in ((0,(0,-1,0)),(1,sub(a[1],a[0])),(2,(0,1,0)),(3,sub(a[0],a[1]))):
                j=(k+1)%4;self.face([a[k],b[k],b[j],a[j]],cell,n)
        self.face(rings[0],cell,sub(path[0],path[1]));self.face(rings[-1],cell,sub(path[-1],path[-2]))
    def extend(self,other):
        start=len(self.positions);self.positions+=other.positions;self.normals+=other.normals;self.uvs+=other.uvs;self.indices += [start+i for i in other.indices]


def junction_outline(role,w):
    points=[(-4-w,-4),(-4+w,-4),(-4+w,-3),(0,.1),(4-w,-3),(4-w,-4),(4+w,-4),(4+w,-3),(w,2),(w,4),(-w,4),(-w,2),(-4-w,-3)]
    return [(x,-z if role=='split' else z) for x,z in points]


def polygon_prism(mesh,outline,bottom,top,cell):
    # Ear clipping preserves the actual concave Y outline, including the inlet gap.
    area=sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(outline,outline[1:]+outline[:1]))
    points=outline if area>0 else list(reversed(outline));remaining=list(range(len(points)))
    def turn(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    while len(remaining)>2:
        for j,i in enumerate(remaining):
            prev,nxt=remaining[j-1],remaining[(j+1)%len(remaining)];a,b,c=[points[k] for k in (prev,i,nxt)]
            if turn(a,b,c)<=1e-9:continue
            if any(all(v>=-1e-9 for v in (turn(a,b,points[k]),turn(b,c,points[k]),turn(c,a,points[k]))) for k in remaining if k not in (prev,i,nxt)):continue
            for y,n in ((top,(0,1,0)),(bottom,(0,-1,0))):mesh.face([(x,y,z) for x,z in (a,b,c)],cell,n)
            remaining.pop(j);break
        else:raise ValueError('Invalid conveyor junction outline')
    for a,b in zip(points,points[1:]+points[:1]):
        mesh.face([(a[0],bottom,a[1]),(b[0],bottom,b[1]),(b[0],top,b[1]),(a[0],top,a[1])],cell,(b[1]-a[1],0,a[0]-b[0]))


def junction(mesh,role):
    polygon_prism(mesh,junction_outline(role,1.4),5.2,5.57,1)
    polygon_prism(mesh,junction_outline(role,1),5.6,5.8,2)
    outline=junction_outline(role,1.3)
    for a,b in zip(outline,outline[1:]+outline[:1]):
        if abs(a[1])==4 and a[1]==b[1]:continue
        a=(a[0],5.99,max(-3.85,min(3.85,a[1])));b=(b[0],5.99,max(-3.85,min(3.85,b[1])))
        mesh.beam(a,b,.16,.62,0)


def module_records():
    result=copy.deepcopy(json.loads(KIT.read_text(encoding='utf-8'))['modules'])
    curve=copy.deepcopy(next(m for m in result if m['shape']=='quarter_circle'))
    curve['assetId']+='.right';curve['slug']+='_right';curve['hand']='right';curve['footprintCenterXZ']=[2,2]
    for p in curve['ports']:
        p['positionMeters'][0]*=-1
        p['facing']={'E':'W','W':'E'}.get(p['facing'],p['facing'])
    result.insert(3,curve)
    for original in list(result):
        if original['role']=='structure':
            raised=copy.deepcopy(original);raised['assetId']+='.tier1';raised['slug']+='_tier1';raised['baseTier']=1;raised['supportTier']=1
            for socket in raised['structuralSockets']:
                if socket['name']=='carrier':socket['positionMeters'][1]+=2
            result.append(raised)
    return result


def paths_for(m,lod):
    if m['shape']=='quarter_circle':
        hand=-1 if m.get('hand')=='right' else 1
        # Centre (0,0) in XZ: (-4,0) tangent +Z -> (0,4) tangent +X.
        steps=(16,10,6)[lod]
        return [[(-hand*4*math.cos(i*math.pi/(2*steps)),5.8,4*math.sin(i*math.pi/(2*steps))) for i in range(steps+1)]]
    if m['role'] in ('merge','split'):
        if m['role']=='merge': return [[(-4,5.8,-4),(-4,5.8,-3),(-2,5.8,0),(0,5.8,1.7)],[(4,5.8,-4),(4,5.8,-3),(2,5.8,0),(0,5.8,1.7)],[(0,5.8,1.7),(0,5.8,4)]]
        return [[(0,5.8,-4),(0,5.8,-1.7)],[(0,5.8,-1.7),(-2,5.8,0),(-4,5.8,3),(-4,5.8,4)],[(0,5.8,-1.7),(2,5.8,0),(4,5.8,3),(4,5.8,4)]]
    if m['ports'] and m['role']!='closure':return [[tuple(m['ports'][0]['positionMeters']),tuple(m['ports'][-1]['positionMeters'])]]
    return []


def path_point(path,distance):
    for a,b in zip(path,path[1:]):
        length=math.dist(a,b)
        if distance<=length:return add(a,mul(sub(b,a),distance/length)),unit(sub(b,a))
        distance-=length
    return path[-1],unit(sub(path[-1],path[-2]))


def bounds(mesh):return [[min(p[k] for p in mesh.positions) for k in range(3)],[max(p[k] for p in mesh.positions) for k in range(3)]]


def extend_support(mesh,m):
    rise=2*m.get('supportTier',0)
    if not rise:return
    for i,p in enumerate(mesh.positions):
        if p[1]>.3:mesh.positions[i]=(p[0],.3+(p[1]-.3)*(4.9+rise)/4.9,p[2])
    for i in range(0,len(mesh.indices),3):
        ids=mesh.indices[i:i+3];a,b,c=[mesh.positions[k] for k in ids];n=unit(cross(sub(b,a),sub(c,a)))
        for k in ids:mesh.normals[k]=n


def assemble(m,state,lod):
    static=Mesh();moving=[];paths=paths_for(m,lod);destroyed=state=='destroyed'
    cellsides=(12,8,6)[lod]
    if m['role']=='structure':
        feet=[0] if m['shape']=='single_column' else [-1.6,1.6]
        for x in feet:
            static.box((x-.4,0,-.4),(x+.4,.3,.4),6)
            static.box((x-.21,.3,-.2),(x+.21,5.2,.2),0)
            static.box((x-.34,4.8,-.32),(x+.34,5.2,.32),1)
            if lod==0:
                for dx in (-.26,.26):
                    for z in (-.26,.26):static.cylinder((x+dx,.29,z),(x+dx,.35,z),.045,7,6)
        if len(feet)>1:
            static.box((-2.05,5.05,-.45),(2.05,5.2,.45),1)
            static.beam((-1.35,4.05,0),(1.35,4.85,0),.13,.13)
            static.beam((-1.35,4.85,0),(1.35,4.05,0),.13,.13)
        else:static.box((-.4,5.04,-.4),(.4,5.2,.4),1)
    elif m['role']=='closure':
        static.box((-1.4,5.2,-.4),(1.4,5.6,.4),1)
        static.box((-1.35,5.6,-.2),(1.35,6.3,.2),5)
        static.box((-1.1,5.64,-.4),(1.1,5.81,.4),2)
    else:
        for lane,path in enumerate(paths):
            # Junction branches share a single filled junction plate; never stack coplanar belt faces.
            if m['role'] not in ('merge','split'):
                static.ribbon(path,2.8,-.6,-.23,1)
                static.ribbon(path,2,-.2,0,2)
            for sign in (-1,1):
                rail=[]
                for i,p in enumerate(path):
                    d=unit(sub(path[min(i+1,len(path)-1)],path[max(0,i-1)]));rail.append(add(p,mul(unit(cross((0,1,0),d)),sign*1.25)))
                if m['role'] not in ('merge','split'):static.ribbon(rail,.25,.08,.5,0 if sign<0 else 1)
            length=sum(math.dist(a,b) for a,b in zip(path,path[1:]))
            for end,d in ((path[0],unit(sub(path[1],path[0]))),(path[-1],unit(sub(path[-1],path[-2])))):
                side=unit(cross((0,1,0),d));center=add(add(end,mul(d,.16 if end==path[0] else -.16)),(0,-.15,0))
                static.cylinder(add(center,mul(side,-.95)),add(center,mul(side,.95)),.145,7,cellsides)
                # Cover the repeated cleat loop seam without extending through the mating plane.
                depth=1.1 if m['shape']=='quarter_circle' else .4
                inset=depth/2+.03;cover=add(end,mul(d,inset if end==path[0] else -inset));static.beam(add(cover,mul(side,-1.12)),add(cover,mul(side,1.12)),depth,.12,1)
            if lod<2:
                for distance in [i*.85+.55 for i in range(max(1,int(length/.85)-1))]:
                    p,d=path_point(path,min(distance,length-.45));side=unit(cross((0,1,0),d))
                    center=add(p,(0,-.3,0));static.cylinder(add(center,mul(side,-1)),add(center,mul(side,1)),.09,3,cellsides)
            # Direction chevrons remain visible on static far LODs and paused belt states.
            for distance in [length*.3,length*.65]:
                p,d=path_point(path,distance);side=unit(cross((0,1,0),d));p=add(p,(0,.013,0))
                for s in (-1,1):static.beam(add(p,mul(side,s*.7)),add(p,mul(d,.35)),.09,.018,5)
            if lod<2:
                pitch=.8;edge=.95 if m['shape']=='quarter_circle' else .12
                count=max(1,int((length-2*edge)/pitch));step=(length-2*edge)/count
                for i in range(count):
                    distance=edge+i*step;p,d=path_point(path,distance);side=unit(cross((0,1,0),d));slat=Mesh()
                    center=add(p,(0,.035,0));slat.beam(add(center,mul(side,-.88)),add(center,mul(side,.88)),.08,.045,2)
                    samples=[]
                    for k in range(9):
                        q,_=path_point(path,distance+step*k/8);samples.append(mul(sub(q,p),1/FRAME))
                    moving.append((f'belt_{lane}_{i}',slat,samples))
        if m['role'] in ('merge','split'):
            junction(static,m['role'])
        p=paths[0][0];x,y,z=p
        static.box((x+.96,y-.46,z+.25),(x+1.35,y-.05,z+.9),0)
        if m['role'] in ('building_to_belt','belt_to_building'):
            z=-1.25 if m['role']=='building_to_belt' else 1.25
            static.box((-1.3,5.8,z-.35),(-1.05,6.3,z+.35),5)
            static.box((1.05,5.8,z-.35),(1.3,6.3,z+.35),5)
            if m['role']=='building_to_belt':static.box((-1.3,6.12,z-.35),(1.3,6.3,z+.35),0)
            else:static.box((-.7,5.82,z-.22),(.7,6.05,z+.22),7)
        if state in ('blocked','unpowered','disconnected'):
            p=paths[-1][-1];x,y,z=add(p,mul(unit(sub(paths[-1][-2],p)),.45))
            if state=='disconnected':
                for sx in (-1,1):static.box((x+sx*1.05-.1,y+.03,z-.16),(x+sx*1.05+.1,y+.47,z+.16),5)
            else:static.box((x-1,y+.02,z-.1),(x+1,y+.44,z+.1),5 if state=='blocked' else 1)
        if state=='congested':
            for i in range(3):
                p,_=path_point(paths[-1],.4+i*.42);x,y,z=p;static.box((x-.65,y+.05,z-.15),(x+.65,y+.48,z+.15),10)
    if m['role']=='structure':extend_support(static,m)
    if state in ('damaged','critical'):
        top=max(p[1] for p in static.positions);static.beam((-.8,top-.4,0),(.8,top-.15,.2),.13,.13,8)
        if state=='critical':static.beam((-.7,top-.3,-.3),(.6,top+.18,.45),.18,.2,8)
        # Bent existing guard tips, not just an added damage decoration.
        for i,p in enumerate(static.positions):
            if p[0]>.8 and p[1]>5.9:static.positions[i]=(p[0]+.08,p[1]-.1 if state=='damaged' else p[1]-.23,p[2])
        # Recompute normals after bounded deformation using the original triangle winding.
        for i in range(0,len(static.indices),3):
            tri=static.indices[i:i+3];a,b,c=[static.positions[k] for k in tri];n=unit(cross(sub(b,a),sub(c,a)))
            for k in tri:static.normals[k]=n
    # Finish is encoded in UV cell choice; no runtime material mutation or shader
    # mask binding. Keep foundation/rubber properties even on damaged modules.
    if state in ('damaged','critical'):
        target=8 if state=='damaged' else 11
        for i,(u,v) in enumerate(static.uvs):
            if int(u*4)+4*int(v*4)==0:
                static.uvs[i]=(u+(target%4)*.25,v+(target//4)*.25)
    if not destroyed:
        if paths:
            x,y,z=paths[0][0]
            # Printed top-facing status on existing service drive, away from belt.
            static.box((x+1.0,y-.045,z+.29),(x+1.31,y-.035,z+.86),STATUS[state])
            static.box((x+1.00,y-.034,z+.29),(x+1.12,y-.024,z+.44),14)
        elif m['role']=='structure':
            x=0 if m['shape']=='single_column' else -1.6
            y=5.2+2*m.get('supportTier',0)
            static.box((x-.22,y+.005,-.22),(x+.22,y+.015,.22),STATUS[state])
        else:
            static.box((-.42,6.305,-.16),(.42,6.315,.16),STATUS[state])
    if destroyed:
        moving=[]
        if paths:
            # Remove a real central span; keep suspended broken end stubs for the later support/placement owner.
            debris=Mesh()
            for path in paths:
                for p,d in [(path[0],unit(sub(path[1],path[0]))),(path[-1],unit(sub(path[-2],path[-1])))]:
                    end=add(p,mul(d,.7));debris.beam(add(p,(0,-.3,0)),add(end,(0,-.5,0)),2.7,.3,8)
            static=debris
        else:
            for i,p in enumerate(static.positions):static.positions[i]=(p[0]+p[1]*.15,p[1]*.35,p[2]+p[1]*.08)
    return [('frame',static,None)]+moving


def glb(path,parts):
    mesh=Mesh();ranges=[]
    for name,part,_ in parts:
        start=len(mesh.indices);mesh.extend(part);ranges.append({'name':name,'firstIndex':start,'indexCount':len(part.indices),'parent':-1,'motion':'static','socket':'origin'})
    doc={'asset':{'version':'2.0','generator':'ForgeLine conveyor model exporter'},'scene':0,'scenes':[{'nodes':[0]}],'nodes':[{'mesh':0,'name':path.stem}],'buffers':[],'bufferViews':[],'accessors':[],'meshes':[{'name':path.stem,'primitives':[{'attributes':{'POSITION':0,'NORMAL':1,'TEXCOORD_0':2},'indices':3,'mode':4}]}]}
    blob=bytearray()
    for data,width,code,kind in [(mesh.positions,3,'f','VEC3'),(mesh.normals,3,'f','VEC3'),(mesh.uvs,2,'f','VEC2'),([(i,) for i in mesh.indices],1,'I','SCALAR')]:
        if data is mesh.positions:data=[mul(p,1/FRAME) for p in data]
        raw=b''.join(struct.pack('<'+code*width,*row) for row in data)
        doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(raw)});blob.extend(raw)
        a={'bufferView':len(doc['bufferViews'])-1,'componentType':5126 if code=='f' else 5125,'count':len(data),'type':kind}
        if kind=='VEC3':a.update(min=[min(p[k] for p in data) for k in range(3)],max=[max(p[k] for p in data) for k in range(3)])
        doc['accessors'].append(a)
    doc['buffers']=[{'byteLength':len(blob)}];encoded=json.dumps(doc,separators=(',',':')).encode();encoded+=b' '*((-len(encoded))%4);blob+=b'\0'*((-len(blob))%4)
    path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(encoded)+len(blob))+struct.pack('<I4s',len(encoded),b'JSON')+encoded+struct.pack('<I4s',len(blob),b'BIN\0')+blob)
    return ranges,bounds(mesh),len(mesh.indices)//3


def clip(path,parts):
    nodes=[{'name':name} for name,_,_ in parts];blob=bytearray();views=[];accessors=[];samplers=[];channels=[]
    def accessor(rows,kind):
        width=len(rows[0]);raw=b''.join(struct.pack('<'+'f'*width,*p) for p in rows);views.append({'buffer':0,'byteOffset':len(blob),'byteLength':len(raw)});blob.extend(raw)
        a={'bufferView':len(views)-1,'componentType':5126,'count':len(rows),'type':kind}
        if kind=='SCALAR':a.update(min=[rows[0][0]],max=[rows[-1][0]])
        accessors.append(a);return len(accessors)-1
    for i,(_,_,samples) in enumerate(parts):
        if not samples:continue
        times=accessor([(k/8,) for k in range(9)],'SCALAR');values=accessor(samples,'VEC3');samplers.append({'input':times,'output':values,'interpolation':'LINEAR'});channels.append({'sampler':len(samplers)-1,'target':{'node':i,'path':'translation'}})
    import base64
    write_json(path,{'asset':{'version':'2.0','generator':'ForgeLine conveyor rigid belt loop'},'nodes':nodes,'scene':0,'scenes':[{'nodes':list(range(len(nodes)))}],'buffers':[{'byteLength':len(blob),'uri':'data:application/octet-stream;base64,'+base64.b64encode(blob).decode()}],'bufferViews':views,'accessors':accessors,'animations':[{'name':'belt_flow','samplers':samplers,'channels':channels}]})


def collision_for(m):
    mesh=Mesh();feet=[]
    if m['role']=='structure':
        for x in ([0] if m['shape']=='single_column' else [-1.6,1.6]):
            mesh.box((x-.4,0,-.4),(x+.4,.3,.4),6);mesh.box((x-.21,.3,-.2),(x+.21,5.2,.2),1);feet.append({'centerXZ':[x,0],'sizeXZ':[.8,.8],'heightMeters':5.2+2*m.get('supportTier',0)})
        if len(feet)>1:mesh.box((-2.05,5.05,-.45),(2.05,5.2,.45),1)
    else:
        paths=paths_for(m,2)
        if not paths:mesh.box((-1.4,5.2,-.4),(1.4,6.3,.4))
        if m['role'] in ('merge','split'):polygon_prism(mesh,junction_outline(m['role'],1.4),5.2,6.3,1)
        else:
            for path in paths:mesh.ribbon(path,2.8,-.6,.5,1)
    if m['role']=='structure':extend_support(mesh,m)
    return mesh,feet


def export_module(m,output,part_provider=assemble,collision_provider=collision_for):
    short=m['assetId'].split('conveyor.',1)[1].replace('.','_');base=m['assetId'];folder=output/short
    states=[v['state'] for v in m['variants']];default='structural_intact' if m['role'] in ('structure','closure') else 'idle_no_input'
    ports=m.get('ports',[]);sockets=[{'name':'origin','x':0,'y':0,'z':0}]+[{'name':p['name'],'x':p['positionMeters'][0]/FRAME,'y':p['positionMeters'][1]/FRAME,'z':p['positionMeters'][2]/FRAME} for p in ports+m.get('structuralSockets',[])]
    rows=[];collision,feet=collision_provider(m);glb(folder/'collision.glb',[('collision',collision,None)])
    write_json(folder/'collision.asset.json',{'id':base+'.collision','type':'mesh','source':'collision.glb'})
    write_json(folder/'selection.asset.json',{'id':base+'.selection','type':'mesh','source':'collision.glb'})
    wreck=part_provider(m,'destroyed',2)[0][1]
    glb(folder/'destroyed_collision.glb',[('collision',wreck,None)])
    write_json(folder/'destroyed_collision.asset.json',{'id':base+'.destroyed.collision','type':'mesh','source':'destroyed_collision.glb'})
    write_json(folder/'destroyed_selection.asset.json',{'id':base+'.destroyed.selection','type':'mesh','source':'destroyed_collision.glb'})
    for state in states:
        for lod in range(3):
            asset=base+'.'+state+('' if lod==0 else '.lod'+str(lod));stem=state+'_lod'+str(lod);parts=part_provider(m,state,lod)
            ranges,limits,tris=glb(folder/(stem+'.glb'),parts)
            definition={'id':asset,'type':'mesh','source':stem+'.glb','materialReferences':[MATERIAL],'collisionRequired':True,'collisionReference':base+('.destroyed.collision' if state=='destroyed' else '.collision'),'sockets':sockets}
            if lod==0:definition['lods']=[{'level':1,'assetId':asset+'.lod1','maxDistance':140},{'level':2,'assetId':asset+'.lod2','maxDistance':420}]
            if lod<2 and state=='flowing':
                definition['mechanicalParts']=ranges;clip(folder/(stem+'.flow.gltf'),parts);cid=asset+'.motion';write_json(folder/(stem+'.flow.asset.json'),{'id':cid,'type':'animation','source':stem+'.flow.gltf'});definition['animationReferences']=[cid]
            write_json(folder/(stem+'.asset.json'),definition)
            rows.append({'state':state,'lod':lod,'assetId':asset,'triangles':tris,'boundsMeters':limits})
            if state==default:
                alias=copy.deepcopy(definition);alias['id']=base+('' if lod==0 else '.lod'+str(lod))
                if lod==0:alias['lods']=[{'level':i,'assetId':base+'.lod'+str(i),'maxDistance':140 if i==1 else 420} for i in (1,2)]
                write_json(folder/('default_lod'+str(lod)+'.asset.json'),alias)
    return {'assetId':base,'slug':m['slug'],'role':m['role'],'baseTier':m['baseTier'],'carrierAttachmentHeightMeters':5.2+2*m.get('supportTier',0),'shape':m['shape'],'footprintMeters':m['footprintMeters'],'footprintCenterXZ':m.get('footprintCenterXZ',[0,0]),'resourceClasses':m['resourceClasses'],'allowedRotationsDegrees':m['allowedRotationsDegrees'],'sourceFrameMeters':FRAME,'instanceScale':[FRAME]*3,'pivotMeters':[0,0,0],'ports':ports,'structuralSockets':m.get('structuralSockets',[]),'collisionAssetId':base+'.collision','groundBlockingPrimitives':feet,'selectionAssetId':base+'.selection','destroyedSelectionAssetId':base+'.destroyed.selection','selectionPolicy':'actual suspended mesh; never use ground-projected elevated AABB as navigation obstacle','clearance':{'minimumMeters':5,'undersideTierZeroMeters':5.2,'validStates':'intact carriers only; supports obstruct passage; damaged/critical/destroyed require actual state geometry checks','appliesToRaisedCarrierOnly':True,'maximumFootingAdjustmentMeters':.2,'heavySupportVehiclePolicy':'2.4m foundation gap is not a truck portal; keep support stations outside vehicle swept lanes','terrainPolicy':'sample maximum terrain under span; reject placement below minimum; supports have rigid local foundations and bounded footing adaptation by future placement system'},'lods':{'distanceMeters':[140,420],'farMotion':'static direction chevrons; no rigid part evaluation at LOD2'},'motion':{'strategy':'existing rigid translation clips for repeated belt cleats','durationSeconds':1,'seam':'repeated pitch reset concealed under terminal covers','activation':'future copied authoritative operating state; only flowing resolves motion assets; idle,blocked,offline and wreck remain static'},'variants':rows}
