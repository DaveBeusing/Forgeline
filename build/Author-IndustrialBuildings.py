"""Export five editable Directorate industrial kits with deterministic LOD/state geometry.

Requires numpy; reuses the established Command Core surface charts and primitives.
The family kit JSON is the editable source. No texture or gameplay data is generated.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]

_geometry_spec = importlib.util.spec_from_file_location('forgeline_source_geometry', Path(__file__).with_name('source_geometry.py'))
source_geometry = importlib.util.module_from_spec(_geometry_spec)
_geometry_spec.loader.exec_module(source_geometry)
SOURCE = ROOT / 'assets/source/buildings/directorate'
FAMILIES = ('extractor', 'smelter', 'electronics_plant', 'fuel_refinery', 'ammunition_plant')
spec = importlib.util.spec_from_file_location('directorate_components', ROOT / 'build/Author-CommandCore.py')
components = importlib.util.module_from_spec(spec)
spec.loader.exec_module(components)


def hopper(mesh, part):
    # Open flared material feed: separate inner/outer walls and a narrow top rim.
    lo, hi = np.asarray(part['min']), np.asarray(part['max'])
    centre = (lo + hi) / 2
    lower = (hi - lo)[[0, 2]] * part.get('throatRatio', .4) / 2
    upper = (hi - lo)[[0, 2]] / 2
    def ring(y, half):
        return [centre + [x * half[0], y - centre[1], z * half[1]]
                for x, z in ((-1,-1),(1,-1),(1,1),(-1,1))]
    a, b = ring(lo[1], lower), ring(hi[1], upper)
    ai, bi = ring(lo[1], lower * .9), ring(hi[1] - .003, upper * .96)
    for i in range(4):
        j = (i + 1) % 4
        outward = np.asarray(a[i]) + np.asarray(a[j]) - centre * 2
        outward[1] = 0
        mesh.face([a[i], a[j], b[j], b[i]], part['cell'], outward)
        mesh.face([ai[i], bi[i], bi[j], ai[j]], 7, -outward)
        mesh.face([b[i], b[j], bi[j], bi[i]], 1, [0,1,0])


def pipe(mesh, part, lod):
    # Swept polyline with continuous circular rings: bends have no intersecting cylinders.
    points = np.asarray(part['points'], float)
    sides = part['sides'][lod]
    rings = []
    reference = np.array(part.get('reference', [0,1,0]), float)
    frames = source_geometry.pipe_frames(points, reference)
    for point, (u, v) in zip(points, frames):
        rings.append([point + part['radius'] * (u*np.cos(k*2*np.pi/sides)+v*np.sin(k*2*np.pi/sides)) for k in range(sides)])
    for i in range(len(points)-1):
        for k in range(sides):
            j = (k+1) % sides
            outward = (rings[i][k] + rings[i][j]) / 2 - points[i]
            mesh.face([rings[i][k],rings[i+1][k],rings[i+1][j],rings[i][j]],part['cell'],outward)
    for ring, outward in ((rings[0],points[0]-points[1]),(rings[-1],points[-1]-points[-2])):
        mesh.face(ring,part['cell'],outward)


def assemble(kit, lod, names):
    mesh = components.Mesh()
    ranges = []
    for module in kit['modules']:
        if module['name'] not in names:
            continue
        start = len(mesh.indices)
        for part in module['parts']:
            if lod not in part['lods']:
                continue
            kind, cell = part['kind'], part['cell']
            if kind == 'box':
                mesh.box(part['min'],part['max'],cell,part.get('bevel',0) if lod==0 else 0)
            elif kind == 'cylinder':
                mesh.cylinder(part['start'],part['end'],part['radius'],cell,part['sides'][lod],part.get('inner',0))
            elif kind == 'hopper':
                hopper(mesh,part)
            elif kind == 'pipe':
                pipe(mesh,part,lod)
            else:
                raise ValueError(f'Unsupported industrial component: {kind}')
        if len(mesh.indices) > start:
            ranges.append(dict(name=module['name'],parent=module.get('parent'),firstIndex=start,indexCount=len(mesh.indices)-start))
    positions = np.asarray(mesh.positions)
    if not len(positions) or not np.isfinite(positions).all():
        raise ValueError('Empty or non-finite industrial mesh')
    if np.any(positions.min(0) < np.asarray(kit['bounds']['min']) - 1e-5) or np.any(positions.max(0) > np.asarray(kit['bounds']['max']) + 1e-5):
        raise ValueError(f"Industrial component exceeds declared bounds: {positions.min(0)}, {positions.max(0)}")
    return mesh, ranges


def export(kit, lod, names, path):
    mesh, ranges = assemble(kit,lod,names)
    report = mesh.export(path)
    data = json.loads(path.read_text(encoding='utf-8'))
    data['asset']['generator'] = 'ForgeLine industrial building component exporter'
    data['extras'] = dict(coordinates=kit['coordinates'],componentHierarchy=ranges,anchors=kit['anchors'])
    path.write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8', newline='\n')
    import hashlib
    report['sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
    return report


def author(family, output):
    kit = json.loads((SOURCE / 'authoring' / f'{family}.kit.json').read_text(encoding='utf-8'))
    reports = {}
    for lod in range(3):
        reports[f'lod{lod}'] = export(kit,lod,kit['intactModules'],output/f'lod{lod}.gltf')
    for name, modules in kit['states'].items():
        reports[name] = export(kit,1,modules,output/f'{name}.gltf')
    for module in kit['modules']:
        reports['kit/'+module['name']] = export(kit,0,[module['name']],output/'kit'/f"{module['name']}.gltf")
    (output/'exports.json').write_text(json.dumps(reports,indent=2)+'\n',encoding='utf-8', newline='\n')
    print(family,json.dumps({name:value['triangles'] for name,value in reports.items() if not name.startswith('kit/')}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--family',choices=FAMILIES,action='append')
    parser.add_argument('--output',type=Path,default=SOURCE/'sources/industrial')
    args = parser.parse_args()
    for family in args.family or FAMILIES:
        author(family,args.output/family)
