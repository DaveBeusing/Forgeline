"""Deterministic form variants through the existing static geometry adapter."""
import argparse
import copy
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import read_geometry, write_glb, write_json, sha, require


def deform(geometry, bend):
    result = copy.deepcopy(geometry)
    for primitive in result['primitives']:
        attributes = primitive['attributes']
        old = attributes['POSITION']
        lo = [min(p[i] for p in old) for i in range(3)]
        hi = [max(p[i] for p in old) for i in range(3)]
        points = [(x + bend * ((y-lo[1])/(hi[1]-lo[1]))**2, y, z) for x,y,z in old]
        minimum, maximum = min(p[0] for p in points), max(p[0] for p in points)
        # Keep the root centred. Both LODs retain the source envelope and ground.
        width = max(abs(minimum), abs(maximum))
        points = [(x * .5/width, y, z) for x,y,z in points]
        normals = [[0.,0.,0.] for _ in points]
        indices = primitive['indices']
        for j in range(0, len(indices), 3):
            ids = indices[j:j+3]
            a,b,c = [points[i] for i in ids]
            u,v = [b[k]-a[k] for k in range(3)], [c[k]-a[k] for k in range(3)]
            n = [u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
            require(sum(x*x for x in n)>1e-18, 'Degenerate variant triangle')
            for i in ids:
                for k in range(3): normals[i][k] += n[k]
        normals = [tuple(x/math.sqrt(sum(v*v for v in n)) for x in n) for n in normals]
        attributes['POSITION'],attributes['NORMAL'] = points,normals
        attributes.pop('TANGENT', None)  # ordinary compiler derives tangent basis from UV0
    return result


def run(source, output):
    recipe = json.loads(Path(__file__).with_name('variants.json').read_text())
    definitions = {}
    for path in sorted(source.rglob('*.asset.json')):
        d = json.loads(path.read_text(encoding='utf-8-sig'))
        require(d['id'] not in definitions, 'Duplicate source ID')
        sidecar=path.with_name(path.name.replace('.asset.json','.scatter.json'))
        if sidecar.is_file():
            require('scatter' not in d,'Duplicate scatter declaration')
            d['scatter']=json.loads(sidecar.read_text())
        definitions[d['id']] = (path,d)
    report = []
    for variant in recipe['variants']:
        for suffix in ('','_lod1'):
            original = variant['familyId']+suffix
            path,d = definitions[original]
            geometry = read_geometry(path.parent/d['source'], source, d)
            changed = deform(geometry,variant['bend'])
            name = variant['id'].split('.')[-1]+suffix
            target = output/'world/meshes/variants'/name
            write_glb(target.with_suffix('.glb'), changed)
            updated = copy.deepcopy(d)
            updated['id'],updated['source'] = variant['id']+suffix,name+'.glb'
            if suffix:
                updated.pop('scatter',None)
            else:
                updated['lods'] = [dict(item,assetId=variant['id']+'_lod1') for item in d['lods']]
                updated['scatter']['familyId'] = variant['familyId']
            write_json(target.with_suffix('.asset.json'), updated)
            report.append(dict(id=updated['id'],sourceId=original,inputSha256=sha(path.parent/d['source']),
                               exportSha256=sha(target.with_suffix('.glb')),
                               triangles=sum(len(p['indices'])//3 for p in changed['primitives'])))
    write_json(output/'variant-exports.json',dict(schemaVersion=1,assets=report,newMaterials=0,newTextures=0))
    return report


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--source',type=Path,default=ROOT/'assets/source')
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    print(json.dumps(run(args.source.resolve(),args.output.resolve())))
