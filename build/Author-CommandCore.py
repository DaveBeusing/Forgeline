"""Export the editable Directorate Command Core component kit to static glTF.

Requires numpy. Inputs, axes, atlas cells and LOD membership are explicit in
assets/source/buildings/directorate/authoring/command_core.kit.json.
Only this family is exported; existing collision and other buildings are retained.
"""
import argparse
import base64
import hashlib
import itertools
import json
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source/buildings/directorate'


class Mesh:
    def __init__(self):
        self.positions, self.normals, self.uvs, self.indices = [], [], [], []

    def face(self, points, cell, outward=None):
        points = np.asarray(points, dtype=float)
        normal = np.cross(points[1] - points[0], points[2] - points[0])
        normal /= np.linalg.norm(normal)
        if outward is not None and np.dot(normal, outward) < 0:
            points = points[::-1]
            normal = -normal
        # Each hard surface has an explicit local chart, inside one atlas cell.
        u_axis = points[1] - points[0]
        u_axis /= np.linalg.norm(u_axis)
        v_axis = np.cross(normal, u_axis)
        coords = np.column_stack((points @ u_axis, points @ v_axis))
        extent = np.maximum(np.ptp(coords, axis=0), 1e-9)
        coords = (coords - coords.min(axis=0)) / extent
        row, column = divmod(cell, 4)
        uv = np.array([column, row]) * .25 + .018 + coords * .214
        first = len(self.positions)
        self.positions.extend(points)
        self.normals.extend([normal] * len(points))
        self.uvs.extend(uv)
        for i in range(1, len(points) - 1):
            self.indices.extend((first, first + i, first + i + 1))

    def box(self, lo, hi, cell, bevel=0):
        lo, hi = np.asarray(lo, float), np.asarray(hi, float)
        centre, half = (lo + hi) / 2, (hi - lo) / 2
        if np.any(half <= 0):
            raise ValueError('Component extents must be positive')
        b = min(bevel, float(half.min() * .45))
        for axis in range(3):
            a, c = [i for i in range(3) if i != axis]
            for sign in (-1, 1):
                points = []
                for sa, sc in ((-1,-1),(1,-1),(1,1),(-1,1)):
                    q = np.zeros(3)
                    q[axis] = sign * half[axis]
                    q[a], q[c] = sa * (half[a]-b), sc * (half[c]-b)
                    points.append(centre + q)
                normal = np.zeros(3); normal[axis] = sign
                self.face(points, cell, normal)
        if not b:
            return
        for a, c in itertools.combinations(range(3), 2):
            axis = next(i for i in range(3) if i not in (a, c))
            for sa, sc in itertools.product((-1,1), repeat=2):
                points = []
                for endpoint, side in ((-1,0),(1,0),(1,1),(-1,1)):
                    q = np.zeros(3); q[axis] = endpoint * (half[axis]-b)
                    q[a] = sa * (half[a] - (b if side else 0))
                    q[c] = sc * (half[c] - (0 if side else b))
                    points.append(centre + q)
                n = np.zeros(3); n[a], n[c] = sa, sc
                self.face(points, cell, n)
        for signs in itertools.product((-1,1), repeat=3):
            points = []
            for axis in range(3):
                q = np.array(signs) * (half-b); q[axis] = signs[axis]*half[axis]
                points.append(centre + q)
            self.face(points, cell, signs)

    def cylinder(self, start, end, radius, cell, sides, inner=0):
        start, end = np.asarray(start, float), np.asarray(end, float)
        axis = end-start; axis /= np.linalg.norm(axis)
        u = np.cross(axis, [0,1,0] if abs(axis[1]) < .9 else [1,0,0])
        u /= np.linalg.norm(u); v = np.cross(axis,u)
        def ring(centre, r, k):
            return centre + r*(u*np.cos(2*np.pi*k/sides)+v*np.sin(2*np.pi*k/sides))
        for k in range(sides):
            j = k+1
            outward = ring(start, 1, k+.5)-start
            self.face([ring(start,radius,k),ring(start,radius,j),ring(end,radius,j),ring(end,radius,k)], cell, outward)
            if inner:
                self.face([ring(start,inner,k),ring(end,inner,k),ring(end,inner,j),ring(start,inner,j)],cell,-outward)
                for centre, n in ((start,-axis),(end,axis)):
                    self.face([ring(centre,inner,k),ring(centre,radius,k),ring(centre,radius,j),ring(centre,inner,j)],cell,n)
        if not inner:
            for centre,n in ((start,-axis),(end,axis)):
                self.face([ring(centre,radius,k) for k in range(sides)],cell,n)

    def dish(self, centre, radius, cell, sides, rings):
        # Double-sided faceted concave receiver tilted towards the loading side.
        centre = np.asarray(centre,float)
        u, v, n = np.array([1,0,0]), np.array([0,.6,-.8]), np.array([0,.8,.6])
        def point(r,k):
            angle=2*np.pi*k/sides
            return centre+r*(u*np.cos(angle)+v*np.sin(angle))+.45*r*r/radius*n
        for r in range(rings):
            a,b=radius*r/rings,radius*(r+1)/rings
            for k in range(sides):
                pts=([centre,point(b,k),point(b,k+1)] if r==0 else
                     [point(a,k),point(b,k),point(b,k+1),point(a,k+1)])
                self.face(pts,cell,n)
                self.face(np.asarray(pts)-.004*n,1,-n)

    def ramp(self, part):
        x0,x1 = part['width']
        z0,z1 = part['depth']
        base,back,front = part['heights']
        points = np.array([[x0,base,z0],[x1,base,z0],[x1,base,z1],[x0,base,z1],
                           [x0,back,z0],[x1,back,z0],[x1,front,z1],[x0,front,z1]])
        for indices,outward in (([0,3,2,1],[0,-1,0]),([4,5,6,7],[0,1,0]),
                                ([0,1,5,4],[0,0,-1]),([3,7,6,2],[0,0,1]),
                                ([0,4,7,3],[-1,0,0]),([1,2,6,5],[1,0,0])):
            self.face(points[indices],part['cell'],outward)

    def export(self, path):
        arrays=[np.asarray(self.positions,'<f4'),np.asarray(self.normals,'<f4'),
                np.asarray(self.uvs,'<f4'),np.asarray(self.indices,'<u4')]
        blob=b''; views=[]; accessors=[]
        for array,kind,component in zip(arrays,('VEC3','VEC3','VEC2','SCALAR'),(5126,5126,5126,5125)):
            data=array.tobytes()
            views.append(dict(buffer=0,byteOffset=len(blob),byteLength=len(data),target=34963 if kind=='SCALAR' else 34962))
            accessor=dict(bufferView=len(views)-1,componentType=component,count=len(array),type=kind)
            if kind=='VEC3':
                accessor.update(min=array.min(0).tolist(),max=array.max(0).tolist())
            accessors.append(accessor); blob+=data
        g=dict(asset=dict(version='2.0',generator='ForgeLine Directorate component exporter'),
               scene=0,scenes=[dict(nodes=[0])],nodes=[dict(mesh=0)],
               buffers=[dict(byteLength=len(blob),uri='data:application/octet-stream;base64,'+base64.b64encode(blob).decode())],
               bufferViews=views,accessors=accessors,meshes=[dict(name=path.stem,primitives=[dict(
                   attributes=dict(POSITION=0,NORMAL=1,TEXCOORD_0=2),indices=3,mode=4)])])
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(json.dumps(g,indent=2)+'\n',encoding='utf-8', newline='\n')
        return dict(vertices=len(arrays[0]),triangles=len(arrays[3])//3,
                    minimum=arrays[0].min(0).tolist(),maximum=arrays[0].max(0).tolist(),
                    sha256=hashlib.sha256(path.read_bytes()).hexdigest())


def assemble(kit, lod, groups=None, state=None):
    mesh=Mesh()
    for module in kit['modules']:
        if groups is not None and module['name'] not in groups:
            continue
        for part in module['parts']:
            if lod not in part['lods'] or (state and state not in part.get('states',[state])):
                continue
            cell=part['cell']; kind=part['kind']
            if kind=='box':
                mesh.box(part['min'],part['max'],cell,part.get('bevel',0) if lod==0 else 0)
            elif kind=='cylinder':
                mesh.cylinder(part['start'],part['end'],part['radius'],cell,part['sides'][lod],part.get('inner',0))
            elif kind=='dish':
                mesh.dish(part['centre'],part['radius'],cell,part['sides'][lod],part['rings'][lod])
            elif kind=='ramp':
                mesh.ramp(part)
            else:
                raise ValueError(f'Unknown component kind: {kind}')
    return mesh


def author(output):
    kit=json.loads((SOURCE/'authoring/command_core.kit.json').read_text())
    report={}
    for lod in range(3):
        mesh=assemble(kit,lod,kit['intactModules'])
        report[f'lod{lod}']=mesh.export(output/f'command_core_assembled_lod{lod}.gltf')
    for module in kit['modules']:
        report[module['name']]=assemble(kit,0,[module['name']]).export(output/'kit'/f"{module['name']}.gltf")
    for name,recipe in kit['states'].items():
        report[name]=assemble(kit,1,recipe).export(output/f'command_core_{name}.gltf')
    (output/'command_core.exports.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8', newline='\n')
    print(json.dumps({name:entry['triangles'] for name,entry in report.items()}))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=SOURCE/'sources/command_core')
    author(parser.parse_args().output)
