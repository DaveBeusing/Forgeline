"""Export the original editable Directorate tank kit and its rigid range metadata.

Requires numpy. Shared hard-surface primitives retain the Directorate atlas UV
convention. Only tank exports and, with --update-metadata, its definitions change.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source/units/directorate'
spec = importlib.util.spec_from_file_location('directorate_geometry', Path(__file__).with_name('Author-CommandCore.py'))
geometry = importlib.util.module_from_spec(spec)
spec.loader.exec_module(geometry)


class Mesh(geometry.Mesh):
    def segment(self, vertex=0, index=0):
        result=Mesh()
        result.positions=list(self.positions[vertex:]); result.normals=list(self.normals[vertex:])
        result.uvs=list(self.uvs[vertex:]); result.indices=[i-vertex for i in self.indices[index:]]
        return result

    def merge(self, other):
        offset = len(self.positions)
        self.positions.extend(other.positions); self.normals.extend(other.normals)
        self.uvs.extend(other.uvs); self.indices.extend(i + offset for i in other.indices)

    def loft(self, profiles, cell, edge):
        rings = []
        for z, width, bottom, top in profiles:
            b = min(edge, (top-bottom)*.24, width*.24)
            if b:
                cross = [(-width+b,bottom),(width-b,bottom),(width,bottom+b),(width,top-b),
                         (width-b,top),(-width+b,top),(-width,top-b),(-width,bottom+b)]
            else:
                cross = [(-width,bottom),(width,bottom),(width,top),(-width,top)]
            rings.append(np.array([[x,y,z] for x,y in cross]))
        for a,b in zip(rings,rings[1:]):
            centre = np.concatenate((a,b)).mean(0)
            for k in range(len(a)):
                j=(k+1)%len(a); points=[a[k],a[j],b[j],b[k]]
                outward=np.mean(points,axis=0)-centre; outward[2]=0
                for ids in ((0,1,2),(0,2,3)):
                    self.face([points[i] for i in ids],cell,outward)
        self.face(rings[0],cell,[0,0,-1]); self.face(rings[-1],cell,[0,0,1])

    def wheel(self, x0, x1, y, z, ry, rz, cell, sides, inner=0):
        piece=Mesh(); piece.cylinder([x0,0,0],[x1,0,0],1,cell,sides,inner)
        scale=np.array([1,ry,rz]); offset=np.array([0,y,z])
        piece.positions=[p*scale+offset for p in piece.positions]
        piece.normals=[(n/scale)/np.linalg.norm(n/scale) for n in piece.normals]
        self.merge(piece)

    def belt(self, profile, x0, x1, inset):
        outer=np.asarray(profile,float); centre=outer.mean(0)
        inner=outer-np.sign(outer-centre)*np.asarray(inset)
        def point(x,q): return np.array([x,q[1],q[0]])
        for k in range(len(outer)):
            j=(k+1)%len(outer); radial=(outer[k]+outer[j])/2-centre
            n=np.array([0,radial[1],radial[0]])
            self.face([point(x0,outer[k]),point(x1,outer[k]),point(x1,outer[j]),point(x0,outer[j])],2,n)
            self.face([point(x0,inner[k]),point(x0,inner[j]),point(x1,inner[j]),point(x1,inner[k])],2,-n)
            for x,sign in ((x0,-1),(x1,1)):
                self.face([point(x,outer[k]),point(x,outer[j]),point(x,inner[j]),point(x,inner[k])],2,[sign,0,0])

    def tread_pads(self, profile, x0, x1, step, thickness):
        if not step: return
        outer=np.asarray(profile,float)
        for a,b in zip(outer,np.roll(outer,-1,axis=0)):
            delta=b-a; length=np.linalg.norm(delta); tangent=delta/length
            outward=np.array([-tangent[1],tangent[0]])
            centre=outer.mean(0)
            if np.dot(outward,(a+b)/2-centre)<0: outward=-outward
            count=max(1,int(np.ceil(length/step))); half=length/count*.39
            for k in range(count):
                mid=a+(k+.5)*delta/count
                corners=[mid-tangent*half,mid+tangent*half,
                         mid+tangent*half+outward*thickness,mid-tangent*half+outward*thickness]
                points=np.array([[x,q[1],q[0]] for x in (x0,x1) for q in corners])
                for indices,n in (([0,3,2,1],[-1,0,0]),([4,5,6,7],[1,0,0]),
                                  ([0,1,5,4],[0,-outward[1],-outward[0]]),
                                  ([3,7,6,2],[0,outward[1],outward[0]]),
                                  ([0,4,7,3],[0,-tangent[1],-tangent[0]]),
                                  ([1,2,6,5],[0,tangent[1],tangent[0]])):
                    self.face(points[indices],1,n)


def boxes(mesh, parts, lod):
    for p in parts:
        if lod in p.get('lods',[0,1,2]):
            mesh.box(p['min'],p['max'],p['cell'],p.get('bevel',0) if lod==0 else 0)


def chassis(kit,lod,modules=None):
    m=Mesh(); hull=kit['hull']; track=kit['tracks']
    m.loft(hull['profiles'],0,hull['edge'][lod]); boxes(m,hull['boxes'],lod)
    if lod==0:
        for z in (-.34,-.30,-.26):
            for x in (-.18,-.06,.06,.18):
                m.box([x-.040,.089,z-.008],[x+.040,.100,z+.008],9)
    if modules is not None: modules['hull']=m.segment()
    wheel_parts=[]; sockets=[]
    for side in (-1,1):
        vertex,index=len(m.positions),len(m.indices)
        x0,x1=sorted(side*np.array(track['xRange']))
        profile=[list(p) for p in track['outerProfile']]
        if lod==2:
            for p in profile:
                if p[1]==-.494: p[1]=-.5
        m.belt(profile,x0,x1,track['innerInset'])
        m.tread_pads(track['outerProfile'],x0-.01,x1+.01,track['padStep'][lod],track['padThickness'])
        for z0,z1 in track['guardSegments'] if lod<2 else [(-.39,.33)]:
            lo,hi=sorted([side*.36,side*.49])
            m.box([lo,-.08,z0],[hi,.015,z1],0,.006 if lod==0 else 0)
        if lod<2:
            for number,z in enumerate(track['wheelCenters']):
                a,b=sorted(side*np.array(track['wheelX']))
                m.wheel(a,b,track['wheelY'],z,*track['wheelRadius'],2,track['sides'][lod],inner=.70 if lod==0 else 0)
                if lod==0:
                    m.wheel(a+.005,b-.005,track['wheelY'],z,
                            track['wheelRadius'][0]*.70,track['wheelRadius'][1]*.70,1,track['sides'][lod])
                    pivot=[side*.43,track['wheelY'],z]
                    part=Mesh(); r=track['hubRadius']
                    part.cylinder([pivot[0]-.052,pivot[1],z],[pivot[0]+.052,pivot[1],z],r,15,12)
                    for angle in (0,np.pi/2,np.pi,3*np.pi/2):
                        y=pivot[1]+r*.58*np.sin(angle); zz=z+r*.58*np.cos(angle)
                        part.cylinder([side*.476,y,zz],[side*.479,y,zz],.004,1,6)
                    name=f'road_wheel_{1 if side<0 else 2}_{number}'
                    wheel_parts.append((name,part,pivot))
                    sockets.append(dict(name='motion_'+name,x=pivot[0],y=pivot[1],z=pivot[2]))
            if lod==0:
                for z in (-.365,.353):
                    a,b=sorted(side*np.array([.397,.473]))
                    m.wheel(a,b,-.35,z,.115,.048,1,track['sides'][lod],inner=.50)
        if lod==0:
            for z in (-.275,-.08,.115,.295):
                lo,hi=sorted(side*np.array([.362,.493]))
                # Guards keep their retained width; hinge studs sit inside it.
                m.box([max(-.49,lo),.015,z-.007],[min(.49,hi),.028,z+.007],1,.003)
        if modules is not None:
            module=m.segment(vertex,index)
            for name,part,pivot in wheel_parts:
                if ('_1_' in name)==(side<0): module.merge(part)
            modules['left_track' if side<0 else 'right_track']=module
    parts=[dict(name='body',firstIndex=0,indexCount=len(m.indices),parent=-1,motion='static')]
    for name,part,pivot in wheel_parts:
        parts.append(dict(name=name,firstIndex=len(m.indices),indexCount=len(part.indices),
                          parent=-1,motion='wheel',socket='motion_'+name,rate=track['rate'],travel=0))
        m.merge(part)
    return m,parts,sockets


def turret(kit,lod,modules=None):
    m=Mesh(); t=kit['turret']; m.loft(t['profiles'],0,t['edge'][lod]); boxes(m,t['boxes'],lod)
    sides=t['cannon']['sides'][lod]
    m.cylinder([0,0,.02],[0,.067,.02],.19,1,sides)
    if lod<2:
        for x,y,z,r in t['hatches']:
            m.cylinder([x,y,z],[x,min(.29,y+.021),z],r,15,sides)
            if lod==0:
                m.box([x-r*.6,y+.019,z-.005],[x+r*.6,y+.03,z+.005],1,.002)
        if lod==0:
            for side in (-1,1):
                for z in (-.08,-.02,.04):
                    x=side*.245
                    m.cylinder([x,.13,z],[x,.21,z+.025],.012,7,8,inner=.007)
                m.box([side*.245-.015,.17,-.08],[side*.245+.015,.185,-.025],1,.002)
    cannon=Mesh(); c=t['cannon']
    cannon.cylinder(c['start'],c['end'],c['radius'],7,sides,c['inner'])
    if lod<2:
        cannon.box([-.067,.117,.075],[.067,.204,.17],0,.007 if lod==0 else 0)
        cannon.cylinder([0,.16,.18],[0,.16,.43],.032,1,sides,inner=.023)
        if lod==0:
            for z in (.195,.415):
                cannon.cylinder([0,.16,z],[0,.16,z+.011],.035,7,sides,inner=.024)
        # Four muzzle-brake shoulders leave an actual opening around the anchor.
        for a,b in (([-.035,.139,.59],[-.013,.181,.64]),([.013,.139,.59],[.035,.181,.64]),
                    ([-.013,.126,.59],[.013,.147,.64]),([-.013,.173,.59],[.013,.194,.64])):
            cannon.box(a,b,7,.002 if lod==0 else 0)
    parts=[dict(name='body',firstIndex=0,indexCount=len(m.indices),parent=-1,motion='static'),
           dict(name='cannon',firstIndex=len(m.indices),indexCount=len(cannon.indices),parent=-1,
                motion='cannon',socket='motion_cannon',rate=1,travel=c['travel'])]
    if modules is not None:
        modules['turret_body']=m.segment(); modules['cannon']=cannon
    m.merge(cannon)
    return m,parts,[dict(name='motion_cannon',x=0,y=.16,z=.10)]


def state_mesh(kit,state):
    m=Mesh()
    if state!='wreck':
        boxes(m,kit['states'][state],0)
        if state=='critical':
            for x in (-.075,0,.075,.15):
                m.box([x-.006,.102,-.32],[x+.006,.135,-.23],1)
        return m
    s=kit['states']['wreck']; m.loft(s['profiles'],8,.008); boxes(m,s['plates'],0)
    a,b,r=s['barrel']; m.cylinder(a,b,r,7,8,.012)
    return m


def author(output,update):
    kit=json.loads((SOURCE/'authoring/main_battle_tank.kit.json').read_text())
    report={}; bindings={}; modules={}
    hull,parts,sockets=chassis(kit,0,modules); gun,gun_parts,gun_sockets=turret(kit,0,modules)
    for name,mesh,part_list,anchors in (('chassis_lod0',hull,parts,kit['sockets']+sockets),
                                       ('turret_lod0',gun,gun_parts,gun_sockets)):
        report[name]=mesh.export(output/f'{name}.gltf'); bindings[name]=dict(mechanicalParts=part_list,sockets=anchors)
    for lod in (1,2):
        m,_,_=chassis(kit,lod); top,_,_=turret(kit,lod); m.merge(top)
        report[f'combined_lod{lod}']=m.export(output/f'combined_lod{lod}.gltf')
    for state in kit['states']:
        report[state]=state_mesh(kit,state).export(output/f'{state}.gltf')
    for name,module in modules.items():
        report['kit.'+name]=module.export(output/'kit'/f'{name}.gltf')
    (output/'exports.json').write_text(json.dumps(dict(meshes=report,bindings=bindings),indent=2)+'\n')
    if update:
        for file,name in (('main_battle_tank','chassis_lod0'),('main_battle_tank_turret','turret_lod0'),
                          ('main_battle_tank_lod1','combined_lod1'),('main_battle_tank_lod2','combined_lod2')):
            path=SOURCE/'vehicles'/f'{file}.asset.json'; d=json.loads(path.read_text())
            d['source']=f'../sources/main_battle_tank/{name}.gltf'
            if name in bindings: d.update(bindings[name])
            path.write_text(json.dumps(d,indent=2)+'\n')
        for state in kit['states']:
            d=dict(id='unit.directorate.main_battle_tank.'+state,type='mesh',
                   source=f'../sources/main_battle_tank/{state}.gltf',
                   materialReferences=['material.directorate.unit.main_battle_tank'])
            (SOURCE/'vehicles'/f'main_battle_tank_{state}.asset.json').write_text(json.dumps(d,indent=2)+'\n')
    print(json.dumps({k:v['triangles'] for k,v in report.items()}))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=SOURCE/'sources/main_battle_tank')
    parser.add_argument('--update-metadata',action='store_true')
    args=parser.parse_args(); author(args.output,args.update_metadata)
