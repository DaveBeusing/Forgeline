"""Bounded procedural atlas recipes and source-only integrity checks."""
import hashlib
import json
import math
from pathlib import Path
from asset_contract import require

FAMILIES = ('painted_armor','gunmetal','rubber','canvas','glass','concrete','heat_steel','grime')
CELLS = dict(zip(FAMILIES,(0,1,2,3,4,6,7,8)))
MAPS = {'base':('baseColor','srgb',2048,5),'normal':('normal','linear',1024,4),'orm':('orm','linear',512,3)}
DEFINITIONS = {'base':'surface_atlas.asset.json','normal':'surface_normal.asset.json','orm':'surface_orm.asset.json'}
SURFACES = Path('materials/directorate/surfaces')


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def validate(config):
    require(set(config)=={'schemaVersion','masterSize','runtimeCaps','seed','recipes'},'Unknown/missing recipe configuration field')
    require(config['schemaVersion']==1 and config['masterSize']==4096,'Canonical master must be 4096 square, schema 1')
    require(set(config['runtimeCaps'])==set(MAPS) and all(type(config['runtimeCaps'][k]) is int and config['runtimeCaps'][k] in (512,1024,2048) and config['runtimeCaps'][k]<=MAPS[k][2] for k in MAPS),'Runtime profile must stay within approved 2K/1K/512 caps')
    require(type(config['seed']) is int and 0<=config['seed']<=2147483647,'Seed must be uint31')
    require(set(config['recipes'])==set(FAMILIES),'All eight material families required')
    for family,r in config['recipes'].items():
        require(set(r)=={'cell','baseLinear','roughness','metallic','heightMetres','frequency'},f'{family}: unknown/missing property')
        require(type(r['cell']) is int and r['cell']==CELLS[family],f'{family}: authoritative atlas cell changed')
        require(isinstance(r['baseLinear'],list) and len(r['baseLinear'])==3,f'{family}: baseLinear requires RGB')
        for key,values,lo,hi in [('baseLinear',r['baseLinear'],0,1),('roughness',[r['roughness']],.08,1),('metallic',[r['metallic']],0,1),('heightMetres',[r['heightMetres']],0,.001),('frequency',[r['frequency']],2,128)]:
            require(all(type(v) in (int,float) and math.isfinite(v) and lo<=v<=hi for v in values),f'{family}: invalid {key}')
        require(r['metallic']==(1 if family in ('gunmetal','heat_steel') else 0),f'{family}: dielectric/metal contract differs')
    return config


def inputs(source, config):
    root=Path(source).resolve();records={}
    for kind,(usage,space,cap,mips) in MAPS.items():
        path=root/SURFACES/DEFINITIONS[kind];d=json.loads(path.read_text(encoding='utf-8-sig'))
        require(d['id']=='texture.directorate.surface.atlas_'+('base' if kind=='base' else kind),f'{path}: stable ID differs')
        require((d['textureUsage'],d['textureColorSpace'])==(usage,space),f'{path}: incorrect usage/color space')
        require(d['textureMaxDimension']==cap and d['textureMaxMipLevels']==mips and d['textureGenerateMipmaps'] is True,f'{path}: runtime budget/mip policy differs')
        require(d['textureFormat']=='bc7Unorm' and d['textureCompressionQuality'] in ('balanced','best'),f'{path}: BC7 quality required')
        image=(path.parent/d['source']).resolve()
        require(image.is_relative_to(root) and image.suffix=='.png',f'{path}: unsafe image source')
        header=image.read_bytes()[:29]
        require(header[:8]==b'\x89PNG\r\n\x1a\n' and int.from_bytes(header[16:20],'big')==4096 and int.from_bytes(header[20:24],'big')==4096 and header[24]==8 and header[25] in (2,6),f'{image}: expected 4K RGB/RGBA8 PNG')
        records[str(path.relative_to(root)).replace('\\','/')]=digest(path)
        records[str(image.relative_to(root)).replace('\\','/')]=digest(image)
    validate(config)
    return records


def generation_key(config, selected, records, toolchain, code):
    return hashlib.sha256(json.dumps([config,sorted(selected),records,toolchain,code],sort_keys=True,allow_nan=False).encode()).hexdigest()


def cache_valid(root, key):
    root=Path(root)
    try:
        report=json.loads((root/'generation.json').read_text())
        require(report['key']==key and bool(report['files']),'Generation key changed')
        for relative,expected in report['files'].items():
            path=(root/relative).resolve()
            require(path.is_relative_to(root.resolve()) and digest(path)==expected,'Damaged output cache')
        return True
    except (OSError,ValueError,KeyError):
        return False
