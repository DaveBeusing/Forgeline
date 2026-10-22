"""Prepared source-only PBR/UV contracts. NOT EXECUTED during implementation.

Runtime compilation, source-less packaging, native material sampling and GPU
costs are independent manual acceptance gates.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'assets/source'
sys.path.insert(0,str(ROOT/'tools/blender'))
from asset_contract import Catalog, read_geometry, require, sha
from pbr_png import read_png

FAMILIES=('ferrous_ore','silicates','volatiles','rare_elements')
VARIANTS=('weathered','fresh_fracture','patinated','inclusion_rich')
FACTORS=((.46,.28,.19,1),(.70,.66,.54,1),(.33,.58,.64,1),(.60,.42,.76,1))


def validate(source_root,geometry_root):
    source_root=Path(source_root).resolve();folder=source_root/'world/authoring/resource_surfaces'
    cfg=json.loads((folder/'recipes.json').read_text());record=json.loads((folder/'sources.json').read_text())
    require(cfg['schemaVersion']==record['schemaVersion']==1,'Unsupported surface contract')
    require(record['recipeSha256']==sha(folder/'recipes.json'),'Recipe inventory is stale')
    require(set(cfg['families'])==set(FAMILIES) and tuple(cfg['surfaceVariants'])==VARIANTS,'Family/variant identity changed')
    require(cfg['legacyUvRectangle']==[[.75,.5],[1,.75]],'Existing mineral atlas rectangle changed')
    require(cfg['runtimeDimensions']=={'base':512,'normal':512,'orm':256},'Review a changed runtime texture allocation separately')
    require(len(record['families'])==4 and {x['family'] for x in record['families']}==set(FAMILIES),'Incomplete native source inventory')
    catalog=Catalog(source_root);base_hashes=set();ids=[]
    for family,factor in zip(FAMILIES,FACTORS):
        recipe=cfg['families'][family]
        require(recipe['baseColorFactor']==list(factor),'Existing family factor changed')
        entry=next(x for x in record['families'] if x['family']==family)
        master=(source_root/entry['nativeSource']).resolve()
        require(master.is_relative_to(folder) and sha(master)==entry['nativeSha256'],'Native material source changed; re-export packed edits')
        require(set(entry['layers'])=={'base','normal','orm','height','zones'},'Incomplete editable surface layers')
        raw={}
        for kind,layer in entry['layers'].items():
            path=(source_root/layer['path']).resolve()
            require(path.is_relative_to(source_root) and sha(path)==layer['sha256'],'Layer source/hash mismatch')
            w,h,channels,pixels=read_png(path)
            require((w,h,channels)==(1024,1024,4),'PBR source extent/format changed')
            raw[kind]=pixels
            if kind in ('base','normal','orm'):require(all(a==255 for a in pixels[3::4]),'PBR alpha must remain opaque')
        base_hashes.add(hashlib.sha256(raw['base']).hexdigest())
        normal=raw['normal'];orm=raw['orm']
        for start in range(0,len(normal),4):
            n=[normal[start+i]/127.5-1 for i in range(3)]
            require(.97<=math.sqrt(sum(v*v for v in n))<=1.03 and n[2]>.70,'Invalid tangent normal length/orientation')
        require(min(orm[0::4])>=208,'AO incorrectly bakes deep large-scale darkness')
        require(min(orm[1::4])>=89 and max(orm[1::4])<=250,'Implausible roughness extent')
        require(max(orm[2::4])<=round(recipe['inclusionMetallic']*255),'Metallicity exceeds the authored inclusion range')
        if family in ('silicates','volatiles'):require(max(orm[2::4])==0,'Dielectric family became metallic')
        # Surface variants must alter spatial content, not merely declare four identical tile IDs.
        tile_hashes=set();pixels=raw['base'];stride=1024*4
        for tile in range(4):
            x=(tile%2)*512*4;y=(tile//2)*512
            region=b''.join(pixels[row*stride+x:row*stride+x+512*4] for row in range(y,y+512))
            tile_hashes.add(hashlib.sha256(region).hexdigest())
        require(len(tile_hashes)==4,'Surface atlas repeats identical patches')
        for kind,usage,space in [('base','baseColor','srgb'),('normal','normal','linear'),('orm','orm','linear')]:
            aid=f'texture.world.resource.{family}.{kind}';ids.append(aid)
            _,d=catalog.definitions[aid]
            require(d['type']=='texture' and d['textureUsage']==usage and d['textureColorSpace']==space,'Texture channel/color-space mismatch')
            require(d['textureGenerateMipmaps'] and d['textureFormat']=='bc7Unorm' and 'textureMaxMipLevels' not in d,'Full-mip BC7 contract changed')
            require(d['textureMaxDimension']==cfg['runtimeDimensions'][kind],'Runtime dimension differs from the declared material allocation')
            require(sha(catalog.source(aid))==entry['layers'][kind]['sha256'],'Descriptor does not reference the authored layer')
        for index,variant in enumerate(VARIANTS):
            aid=f'material.world.resource.{family}.{variant}';ids.append(aid)
            payload=json.loads(catalog.source(aid).read_text())
            require(payload['baseColorFactor']==list(factor),'Material factor changed')
            require(payload['uvScale']==[2,2] and payload['uvOffset']==[-1.5+(index%2)/2,-1+(index//2)/2],'UV quadrant selection differs')
            require(payload['emissiveMultiplier']==0 and not payload.get('emissiveTexture'),'Permanent resource emissive introduced')
            for slot,kind in [('baseColorTexture','base'),('normalTexture','normal'),('ormTexture','orm')]:
                require(payload[slot]==f'texture.world.resource.{family}.{kind}','Material channel points to a foreign texture')
            if index==0:
                default=f'material.world.resource.{family}';ids.append(default)
                require(json.loads(catalog.source(default).read_text())==payload,'Stable material ID is not the weathered default')
    require(len(base_hashes)==4,'Resource families share identical surface maps')
    catalog.validate(catalog.closure(ids))
    # Use the current geometry checkout even when validating an isolated material export.
    geometry=Catalog(Path(geometry_root).resolve())
    for aid,(_,definition) in geometry.definitions.items():
        if definition['type']!='mesh' or not any(x==f'material.world.resource.{family}' for x in definition.get('materialReferences',[]) for family in FAMILIES):continue
        mesh=read_geometry(geometry.source(aid),geometry.root,definition)
        for primitive in mesh['primitives']:
            for u,v in primitive['attributes'].get('TEXCOORD_0',[]):
                require(.76799<=u<=.98201 and .51799<=v<=.73201,'Geometry no longer occupies the agreed inset mineral rectangle')
                for tile in range(4):
                    tu=u*2-1.5+(tile%2)/2;tv=v*2-1+(tile//2)/2
                    require((tile%2)/2+.0359<=tu<=(tile%2)/2+.4641 and (tile//2)/2+.0359<=tv<=(tile//2)/2+.4641,'UV transform crosses a surface quadrant gutter')
    print('Source material/UV contracts passed; native, mip, compiled budget and package acceptance remain separate')


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--source-root',type=Path,default=SOURCE)
    parser.add_argument('--geometry-source-root',type=Path,default=SOURCE)
    args=parser.parse_args();validate(args.source_root,args.geometry_source_root)
