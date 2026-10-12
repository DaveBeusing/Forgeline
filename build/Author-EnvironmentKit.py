"""Export the editable Central Divide environment kit. Requires NumPy.

Geometry recipes, physical placement sizes and atlas cells are explicit inputs.
Only this kit and the selected ferrous mesh definitions are authored.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source/world'
spec = importlib.util.spec_from_file_location('hard_surface', Path(__file__).with_name('Author-CommandCore.py'))
primitive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(primitive)


class Mesh(primitive.Mesh):
    def blob(self, centre, radius, cell, sides, rings, phase=0, irregular=0):
        centre, radius = np.array(centre), np.array(radius)
        def point(ring, side):
            lat = -np.pi/2 + np.pi*ring/rings
            lon = 2*np.pi*side/sides + phase
            variation = 1 + irregular*np.sin(side*2.3 + ring*.7 + phase)
            return centre + radius*np.array([np.cos(lat)*np.cos(lon)*variation,
                np.sin(lat), np.cos(lat)*np.sin(lon)*variation])
        for r in range(rings):
            for k in range(sides):
                if r == 0: points = [point(0,0),point(1,k),point(1,k+1)]
                elif r == rings-1: points = [point(r,k),point(rings,0),point(r,k+1)]
                else: points = [point(r,k),point(r+1,k),point(r+1,k+1),point(r,k+1)]
                for j in range(1,len(points)-1):
                    triangle=[points[0],points[j],points[j+1]]
                    self.face(triangle, cell, np.mean(triangle,axis=0)-centre)

    def fit(self, lo, hi):
        p=np.array(self.positions); lo=np.array(lo); hi=np.array(hi)
        scale=(hi-lo)/np.ptp(p,axis=0)
        self.positions=list(lo+(p-p.min(0))*scale)
        n=np.array(self.normals)/scale
        self.normals=list(n/np.linalg.norm(n,axis=1)[:,None])

    def root_fit(self):
        p=np.array(self.positions)
        scale=np.array([.5/np.max(np.abs(p[:,0])),1/p[:,1].max(),.5/np.max(np.abs(p[:,2]))])
        self.positions=list(p*scale)
        n=np.array(self.normals)/scale
        self.normals=list(n/np.linalg.norm(n,axis=1)[:,None])


def model(recipe, reduced):
    m=Mesh(); shape=recipe['shape']; cells=recipe['cells']
    sides=recipe['sides'][int(reduced)]
    edge=0 if reduced else .006
    if shape == 'rock':
        for c,r,phase in recipe['clusters']:
            m.blob(c,r,cells[0],sides,3 if reduced else 5,phase,.12)
        m.fit(recipe['bounds'][0],recipe['bounds'][1])
    elif shape == 'broadleaf':
        m.cylinder([0,0,0],[0,.68,0],.035,cells[0],sides)
        crowns=recipe['crowns'][::2] if reduced else recipe['crowns']
        for c,r in crowns:
            m.cylinder([0,.36,0],c,.014,cells[0],sides)
            m.blob(c,r,cells[1],sides,3 if reduced else 4)
        m.root_fit()
    elif shape == 'barrier':
        m.ramp(dict(width=[-.5,.5],depth=[-.5,.5],heights=[-.5,-.08,-.08],cell=cells[0]))
        m.ramp(dict(width=[-.5,.5],depth=[-.28,.28],heights=[-.1,.5,.15],cell=cells[0]))
        if not reduced:
            for x in (-.36,.36):
                m.box([x-.025,-.49,-.51],[x+.025,-.28,.51],cells[1],edge)
                m.box([x-.08,.12,-.285],[x+.08,.28,-.275],cells[2])
            m.cylinder([-.08,.51,0],[.08,.51,0],.018,cells[1],sides,inner=.010)
        m.fit([-.5,-.5,-.5],[.5,.5,.5])
    elif shape == 'pipe_elbow':
        steps=recipe['bendSteps'][int(reduced)]; radius=recipe['bendRadius']; tube=recipe['tubeRadius']
        for i in range(steps):
            a,b=i*np.pi/2/steps,(i+1)*np.pi/2/steps
            m.cylinder([-.22+radius*np.cos(a),0,-.22+radius*np.sin(a)],
                [-.22+radius*np.cos(b),0,-.22+radius*np.sin(b)],tube,cells[0],sides,inner=tube*.74)
        for a in (0,np.pi/2):
            c=np.array([-.22+radius*np.cos(a),0,-.22+radius*np.sin(a)])
            tangent=np.array([-np.sin(a),0,np.cos(a)])
            m.cylinder(c-.035*tangent,c+.035*tangent,tube*1.32,cells[1],sides,inner=tube*.74)
            if not reduced:
                for j in range(6):
                    angle=j*np.pi/3
                    offset=np.array([np.cos(a)*np.cos(angle),np.sin(angle),np.sin(a)*np.cos(angle)])*tube*1.12
                    m.cylinder(c+offset-.045*tangent,c+offset+.045*tangent,.012,cells[1],4)
        m.fit([-.5,-.5,-.5],[.5,.5,.5])
    elif shape == 'gantry':
        for x in (-.43,.43):
            m.box([x-.055,-.5,-.3],[x+.055,.4,.3],cells[0],edge)
            m.box([x-.07,-.5,-.4],[x+.07,-.44,.4],cells[1],edge)
            if not reduced:
                m.cylinder([x,-.35,-.26],[x,.35,.26],.018,cells[0],sides)
                for z in (-.22,.22):m.box([x-.058,-.25,z-.04],[x+.058,-.15,z+.04],cells[2])
        for z in (-.22,.22):m.box([-.5,.34,z-.055],[.5,.5,z+.055],cells[0],edge)
        m.box([-.16,.22,-.26],[.16,.36,.26],cells[1],edge)
        m.cylinder([0,.23,0],[0,-.04,0],.015,cells[0],sides)
        m.cylinder([0,-.04,0],[.04,-.09,0],.018,cells[0],sides,inner=.010)
    elif shape == 'road_edge':
        m.ramp(dict(width=[-.5,.5],depth=[-.5,.5],heights=[-.5,.5,-.15],cell=cells[0]))
        m.box([-.5,.15,-.47],[.5,.48,-.35],cells[1],edge)
        if not reduced:
            for x in np.linspace(-.42,.42,9):m.box([x-.016,.20,-.34],[x+.016,.23,.36],cells[2])
    elif shape in ('bridge_apron','loading_apron'):
        m.ramp(dict(width=[-.5,.5],depth=[-.5,.5],heights=[-.5,.23,-.35 if shape=='bridge_apron' else .23],cell=cells[0]))
        for x in (-.46,.46):m.box([x-.035,-.35,-.5],[x+.035,.5,.5],cells[0],edge)
        if not reduced:
            for z in (-.30,.05,.34):m.box([-.425,.232,z-.008],[.425,.24,z+.008],cells[1])
            if shape=='loading_apron':
                for x in (-.3,.3):m.box([x-.02,.24,-.4],[x+.02,.26,.35],cells[2])
    elif shape == 'fence':
        for x in (-.47,.47):
            m.box([x-.03,-.5,-.1],[x+.03,.5,.1],cells[0],edge)
            m.box([x-.05,-.5,-.22],[x+.05,-.43,.22],cells[1])
        for y in (-.28,.32):m.box([-.5,y-.025,-.05],[.5,y+.025,.05],cells[0])
        for x in np.linspace(-.36,.36,5 if reduced else 11):m.box([x-.01,-.30,-.025],[x+.01,.39,.025],cells[0])
        if not reduced:m.box([-.11,.03,-.031],[.11,.25,-.026],cells[2])
    else: raise ValueError(shape)
    return m


def export(m,path):
    report=m.export(path)
    g=json.loads(path.read_text(encoding='utf-8'))
    g['asset']['generator']='ForgeLine environment kit authoring'
    path.write_text(json.dumps(g,indent=2)+'\n',encoding='utf-8')
    report['sha256']=hashlib.sha256(path.read_bytes()).hexdigest()
    return report


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path);parser.add_argument('--update-metadata',action='store_true')
    args=parser.parse_args();kit=json.loads((SOURCE/'authoring/environment.kit.json').read_text(encoding='utf-8'))
    output=args.output or SOURCE/'meshes/sources/environment_kit';output.mkdir(parents=True,exist_ok=True)
    reports={}
    for name,r in kit['families'].items():
        for reduced in (False,True):
            filename=f'{name}_{"reduced" if reduced else "high"}.gltf'
            reports[filename]=export(model(r,reduced),output/filename)
            if args.update_metadata:
                meta=SOURCE/'meshes/kit'/f'{name}{"_lod1" if reduced else ""}.asset.json';meta.parent.mkdir(parents=True,exist_ok=True)
                d=dict(id=r['id']+('_lod1' if reduced else ''),type='mesh',source='../sources/environment_kit/'+filename,materialReferences=[r['material']])
                if not reduced:
                    d.update(lods=[dict(level=1,assetId=r['id']+'_lod1',maxDistance=r['distance'])],
                        collisionReference='mesh.world.kit.collision_rooted' if r['shape']=='broadleaf' else 'mesh.world.kit.collision_centered',collisionRequired=True)
                meta.write_text(json.dumps(d,indent=2)+'\n',encoding='utf-8')
    for rooted in (False,True):
        name='collision_rooted' if rooted else 'collision_centered';m=Mesh()
        m.box([-.5,0 if rooted else -.5,-.5],[.5,1 if rooted else .5,.5],6)
        reports[name+'.gltf']=export(m,output/(name+'.gltf'))
        if args.update_metadata:
            d=dict(id='mesh.world.kit.'+name,type='mesh',source='../sources/environment_kit/'+name+'.gltf')
            (SOURCE/'meshes/kit'/f'{name}.asset.json').write_text(json.dumps(d,indent=2)+'\n',encoding='utf-8')
    for reduced in (False,True):
        r=kit['ferrous'];name='ferrous_reduced' if reduced else 'ferrous_high'
        reports[name+'.gltf']=export(model(r,reduced),output/(name+'.gltf'))
        if args.update_metadata:
            p=SOURCE/'meshes/resources'/f'ferrous_ore{"_lod1" if reduced else ""}.asset.json'
            d=json.loads(p.read_text(encoding='utf-8'));d['source']='../sources/environment_kit/'+name+'.gltf'
            p.write_text(json.dumps(d,indent=2)+'\n',encoding='utf-8')
    (output/'exports.json').write_text(json.dumps(reports,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({name:r['triangles'] for name,r in reports.items()}))


if __name__ == '__main__':main()
