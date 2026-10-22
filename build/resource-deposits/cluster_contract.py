"""Prepared portable geometry contracts. Not executed during source authoring.

Run manually: python build/resource-deposits/cluster_contract.py
This reads source geometry only; Blender reopening, compiler and GPU qualification
are independent manual gates.
"""
import hashlib
import argparse
import json
import math
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'assets/source'
MASTERS=SOURCE/'world/authoring/resource_clusters'
sys.path.insert(0,str(ROOT/'tools/blender'))
from asset_contract import Catalog, read_geometry, require, sha


def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def dot(a,b): return sum(x*y for x,y in zip(a,b))


def historical():
    # Original native sources and their original GLB copies remain auditable history.
    # Their frozen signatures qualify the old bindings, not the new authored clusters.
    for name in ('ferrous_ore','silicates','volatiles','rare_elements'):
        folder=ROOT/'assets/staged/resource-deposits'/name
        lineage=json.loads((folder/'lineage.json').read_text())
        for item in lineage['masters']:
            require(sha(folder/item['path'])==item['sha256'],'Historical Blender master changed')
        for item in lineage['exports']:
            require(sha(folder/item['source'])==item['sha256'],'Historical GLB changed')
            original=SOURCE/'world/meshes/sources/blender'/name/item['source']
            require(sha(original)==item['sha256'],'Historical canonical GLB copy changed')


def validate(source_root=SOURCE):
    source_root=Path(source_root).resolve();masters=source_root/'world/authoring/resource_clusters'
    cfg=json.loads((masters/'variants.json').read_text())
    record=json.loads((masters/'sources.json').read_text())
    require(cfg['schemaVersion']==1 and record['schemaVersion']==1,'Unsupported cluster contract')
    require(set(record['families'])==set(cfg['families']),'Incomplete family set')
    require(len(record['masterSources'])==4,'Expected four native master projects')
    for master in record['masterSources']:
        path=(source_root/master['path']).resolve()
        require(path.is_relative_to(masters),'Native source path escapes authoring directory')
        require(sha(path)==master['sha256'],'Saved cluster master changed; export saved edits before qualification')
    catalog=Catalog(source_root)
    expected=set()
    for family,description in cfg['families'].items():
        require(len(description['layouts'])==len(set(description['layouts']))==4,'Expected four distinct layouts')
        for layout in description['layouts']:
            for size in cfg['sizes']:
                base=f'mesh.world.resource.{family}.{layout}.{size}'
                expected.update((base,base+'_lod1',base+'_lod2',base+'.collision'))
    ids=[row['id'] for row in record['exports']]
    require(len(ids)==len(set(ids))==192 and set(ids)==expected,'Expected 144 render meshes and 48 proxies')
    bounds={}; fingerprints={}
    for row in record['exports']:
        path,definition=catalog.definitions[row['id']]
        source=catalog.source(row['id'])
        require(source.relative_to(source_root).as_posix()==row['source'] and sha(source)==row['sha256'],'GLB binding/hash mismatch')
        geometry=read_geometry(source,source_root,definition)
        triangles=sum(len(p['indices'])//3 for p in geometry['primitives'])
        require(triangles==row['triangles'],'Authored triangle inventory is stale')
        require(triangles<= (12 if row['lod']==-1 else cfg['sourceTriangleCeilings'][row['lod']]),'Source geometry ceiling exceeded')
        points=[]
        for primitive in geometry['primitives']:
            attrs=primitive['attributes'];points.extend(attrs['POSITION'])
            require(set(attrs)=={'POSITION','NORMAL','TEXCOORD_0','TANGENT'},'Missing vertex channel')
            for values in attrs.values():
                require(all(math.isfinite(x) for value in values for x in value),'Nonfinite geometry channel')
            for n,t in zip(attrs['NORMAL'],attrs['TANGENT']):
                require(abs(dot(n,n)-1)<.001 and abs(dot(t[:3],t[:3])-1)<.001 and abs(dot(n,t[:3]))<.001,'Invalid tangent basis')
                require(t[3] in (-1,1),'Invalid tangent handedness')
            for start in range(0,len(primitive['indices']),3):
                a,b,c=[primitive['indices'][start+i] for i in range(3)]
                normal=cross(sub(attrs['POSITION'][b],attrs['POSITION'][a]),sub(attrs['POSITION'][c],attrs['POSITION'][a]))
                require(dot(normal,normal)>1e-16,'Degenerate triangle')
                require(dot(normal,attrs['NORMAL'][a])>0,'Normal/winding mismatch')
        lo=[min(p[k] for p in points) for k in range(3)];hi=[max(p[k] for p in points) for k in range(3)]
        require(abs(lo[1]+.5)<1e-5 and all(-.50001<=x<=.50001 for p in points for x in p),'Ground anchor/envelope mismatch')
        require(max(abs(a-b) for actual,saved in zip((lo,hi),row['bounds']) for a,b in zip(actual,saved))<1e-5,'Stale bounds record')
        bounds[row['id']]=(lo,hi)
        if row['lod']>=0:
            material=f'material.world.resource.{row["family"]}'
            require(definition['materialReferences']==[material],'Family material identity changed')
            proxy=f'mesh.world.resource.{row["family"]}.{row["layout"]}.{row["size"]}.collision'
            require(definition['collisionReference']==proxy,'Missing matching proxy')
        if row['lod']==0:
            # Rotation-invariant radial/height signature rejects yaw-only duplicates.
            unique=set(tuple(p) for p in points)
            signature=sorted((round(p[1],5),round(p[0]*p[0]+p[2]*p[2],5)) for p in unique)
            fingerprint=hashlib.sha256(json.dumps(signature).encode()).hexdigest()
            key=(row['family'],row['size']);fingerprints.setdefault(key,set()).add(fingerprint)
    require(all(len(values)==4 for values in fingerprints.values()),'Layouts collapse to yaw-only copies')
    for row in record['exports']:
        if row['lod']<0:continue
        _,definition=catalog.definitions[row['id']]
        proxy_lo,proxy_hi=bounds[definition['collisionReference']];lo,hi=bounds[row['id']]
        require(all(proxy_lo[k]-1e-5<=lo[k] and hi[k]<=proxy_hi[k]+1e-5 for k in range(3)),'Proxy does not contain render geometry')
    for family,description in cfg['families'].items():
        base=f'mesh.world.resource.{family}';variant=base+f'.{description["defaultLayout"]}.medium'
        for lod in range(3):
            suffix='' if lod==0 else f'_lod{lod}'
            require(sha(catalog.source(base+suffix))==sha(catalog.source(variant+suffix)),'Legacy ID does not resolve default cluster')
            _,alias=catalog.definitions[base+suffix]
            _,definition=catalog.definitions[variant+suffix]
            require(alias['materialReferences']==definition['materialReferences'],'Legacy material changed')
            if lod==0:
                require(alias['lods']==[{'level':i,'assetId':base+f'_lod{i}','maxDistance':260 if i==1 else 520} for i in (1,2)],'Legacy LOD binding changed')
    historical()
    print('Source-only cluster contracts passed: 4 families, 48 layouts/sizes, 144 render meshes, 48 proxies; native/runtime acceptance remains separate')


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--source-root',type=Path,default=SOURCE)
    validate(parser.parse_args().source_root)
