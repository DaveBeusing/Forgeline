"""Rebuild lossless physical surface masters. Requires numpy and Pillow.

All fields are authored at their final resolution from periodic mathematical
surfaces. Base color contains reflectance only; normals and ORM share the same
height/wear fields. No photograph, resize or source-image input is used.
"""
from pathlib import Path
import argparse
import json
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]


def noise(n, cells, seed):
    rng = np.random.default_rng(seed)
    grid = rng.uniform(-1, 1, (cells, cells)).astype(np.float32)
    p = np.arange(n, dtype=np.float32) * cells / n
    lo = p.astype(int) % cells
    hi = (lo + 1) % cells
    t = p - np.floor(p)
    t = t * t * (3 - 2 * t)
    a = grid[lo[:, None], lo[None, :]]
    b = grid[lo[:, None], hi[None, :]]
    c = grid[hi[:, None], lo[None, :]]
    d = grid[hi[:, None], hi[None, :]]
    return (a * (1-t)[None, :] + b*t[None, :]) * (1-t)[:, None] + (c*(1-t)[None, :] + d*t[None, :])*t[:, None]


def normal(height, strength):
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * strength
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * strength
    v = np.stack((-dx, -dy, np.ones_like(dx)), axis=2)
    v /= np.linalg.norm(v, axis=2)[:, :, None]
    return np.clip(np.rint((v * .5 + .5) * 255), 0, 255).astype(np.uint8)


def save(path, pixels):
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.clip(np.rint(pixels), 0, 255).astype(np.uint8)).save(path, compress_level=9)


def definition(path, asset_id, source, usage, cap, compressed=True, mips=None):
    data = dict(id=asset_id, type='texture', source=source, textureUsage=usage,
                textureColorSpace='srgb' if usage == 'baseColor' else 'linear',
                textureGenerateMipmaps=True, textureMaxDimension=cap,
                textureFormat='bc7Unorm' if compressed else 'rgba8Unorm')
    if mips:
        data['textureMaxMipLevels'] = mips
    if compressed:
        data['textureCompressionQuality'] = 'balanced'
    path.write_text(json.dumps(data, indent=2) + '\n')


def atlas():
    n = 1024
    y, x = np.mgrid[:n, :n].astype(np.float32) / n
    palette = [(87,94,65),(58,64,65),(29,32,32),(91,91,62),
               (28,54,65),(173,142,42),(145,145,134),(73,68,61),
               (78,84,56),(49,55,56),(109,83,53),(119,115,104),
               (45,64,38),(73,83,43),(113,109,62),(71,79,57)]
    rough = [.72,.56,.94,.94,.23,.78,.91,.65,.87,.68,.90,.90,.96,.96,.96,.73]
    metal = [.12,.88,0,0,0,.05,0,.85,.32,.8,0,0,0,0,0,.14]
    colors, heights, orms = [], [], []
    for i, color in enumerate(palette):
        grain = noise(n, 128, 71+i)
        macro = noise(n, 8, 401+i)
        h = grain * .008
        variation = grain*2 + macro*5
        wear = np.zeros((n,n), dtype=np.float32)
        if i in (0,1,4,7,8,9,15):
            # Panel gaps and flush fasteners are actual height features.
            gap = (np.abs((x+.015) % .5-.25) > .244) | (np.abs((y+.015) % .5-.25) > .244)
            h -= gap*.035
            variation -= gap*16
            edge = (np.abs((x+.015) % .5-.25) > .237) | (np.abs((y+.015) % .5-.25) > .237)
            wear += (edge & ~gap)*.65
            for cx in (.055,.445,.555,.945):
                for cy in (.055,.445,.555,.945):
                    r = np.sqrt((x-cx)**2+(y-cy)**2)
                    ring = (r < .012) & (r > .007)
                    h += ring*.024
                    variation -= ring*17
                    slot = (r < .007) & (np.abs(x-cx) < .0015)
                    h -= slot*.015
            if i == 9:
                vent = (x>.12)&(x<.88)&(y>.18)&(y<.8)&((y % .045)<.018)
                h -= vent*.04
                variation -= vent*22
            if i == 8:
                wear += np.clip((noise(n,64,80)-.5)*3,0,1)
        elif i == 2:
            tread = ((x % .125)<.017) | ((y+.3*x) % .125 < .02)
            h -= tread*.06
            variation -= tread*8
        elif i == 3:
            # Woven warp/weft thread relief at the native master scale.
            weave = np.sin(x*np.pi*256)*np.sin(y*np.pi*256)
            h += weave*.012
            variation += weave*5
        elif i == 5:
            stripes = ((x+y) % .28)<.14
            variation -= stripes*125
            wear = np.clip((grain-.3)*2,0,1)
        elif i == 6:
            pits = np.clip((noise(n,256,88)-.4)*2,0,1)
            h -= pits*.026
            variation -= pits*17
        elif i == 10:
            seams = (y % .2)<.012
            grainwood = np.sin(x*300 + noise(n,8,22)*5)
            h += grainwood*.009-seams*.05
            variation += grainwood*6-seams*32
        elif i == 11:
            h += noise(n,32,77)*.075
            variation += noise(n,32,77)*15
        elif i in (12,13,14):
            mask=Image.new('RGB',(n,n),(0,0,0))
            draw=ImageDraw.Draw(mask)
            rng=np.random.default_rng(180+i)
            for _ in range(120 if i!=14 else 650):
                cx,cy=rng.uniform(-80,n+80,2)
                angle=rng.uniform(0,np.pi*2)
                length=rng.uniform(50,170) if i!=14 else rng.uniform(30,100)
                tip=(cx+np.cos(angle)*length,cy+np.sin(angle)*length)
                draw.line([(cx,cy),tip],fill=(70,70,70),width=3)
                if i==14:
                    side=np.array([-np.sin(angle),np.cos(angle)])*rng.uniform(2,5)
                    draw.polygon([(cx,cy),tuple(np.array([cx,cy])+side),tip],fill=(160,160,160))
                else:
                    for t in np.linspace(.1,.95,14 if i==12 else 6):
                        stem=np.array([cx,cy])+np.array([np.cos(angle),np.sin(angle)])*length*t
                        for sign in (-1,1):
                            a=angle+sign*(.65 if i==12 else 1.0)
                            end=stem+np.array([np.cos(a),np.sin(a)])*(18 if i==12 else 24)*(1-t*.45)
                            value=int(rng.integers(90,230))
                            if i==12: draw.line([tuple(stem),tuple(end)],fill=(value,value,value),width=2)
                            else:
                                side=np.array([-np.sin(a),np.cos(a)])*6
                                middle=(stem+end)/2
                                draw.polygon([tuple(stem),tuple(middle+side),tuple(end),tuple(middle-side)],fill=(value,value,value))
            botanical=np.asarray(mask,dtype=np.float32)[:,:,0]/255
            h+=botanical*.045
            variation+=botanical*38-8
        c = np.asarray(color,dtype=np.float32)[None,None,:] + variation[:,:,None]
        if i in (0,8,15):
            c = c*(1-wear[:,:,None]*.5) + np.array([102,100,87])*wear[:,:,None]*.5
        ao = np.clip(1+np.minimum(h,0)*2,.78,1)
        r = np.clip(rough[i]+macro*.025+grain*.012+wear*.08,.12,.99)
        m = np.clip(metal[i]+wear*.35,0,1) if i in (0,8,15) else np.full_like(h,metal[i])
        colors.append(c); heights.append(h); orms.append(np.stack((ao,r,m),2)*255)
    path = ROOT/'assets/source/materials/directorate/surfaces'
    def assemble(items):
        return np.concatenate([np.concatenate(items[r*4:r*4+4],1) for r in range(4)],0)
    save(path/'production_surface_base.png',assemble(colors))
    # Different cells must not influence one another's derivatives.
    save(path/'production_surface_normal.png',assemble([normal(h,12) for h in heights]))
    save(path/'production_surface_orm.png',assemble(orms))
    definition(path/'surface_atlas.asset.json','texture.directorate.surface.atlas_base','production_surface_base.png','baseColor',2048,True,5)
    definition(path/'surface_normal.asset.json','texture.directorate.surface.atlas_normal','production_surface_normal.png','normal',1024,True,4)
    definition(path/'surface_orm.asset.json','texture.directorate.surface.atlas_orm','production_surface_orm.png','orm',512,True,3)
    for p in (ROOT/'assets/source').rglob('*.material.json'):
        data=json.loads(p.read_text())
        if data.get('baseColorTexture') == 'texture.directorate.surface.atlas_base':
            data['normalTexture']='texture.directorate.surface.atlas_normal'
            p.write_text(json.dumps(data,indent=2)+'\n')


def terrain(only=None):
    n=4096
    y,x=np.mgrid[:n,:n].astype(np.float32)/n
    path=ROOT/'assets/source/world/textures/terrain'
    families=[('dry_dirt',(133,108,76),.92),('cracked_earth',(98,79,57),.86),
              ('rocky_scrub',(106,109,85),.91),('dark_ash',(67,65,58),.97),
              ('concrete_base',(152,150,138),.88),
              ('bedrock',(129,127,117),.84),('crushed_gravel',(122,118,104),.93)]
    for i,(name,color,rough) in enumerate(families):
        if only and name not in only: continue
        macro=noise(n,8,500+i)
        aggregate=noise(n,128,600+i)
        micro=noise(n,1024,700+i)
        h=macro*.06+aggregate*.035+micro*.003
        shade=macro*12+aggregate*9+micro*2
        if name=='cracked_earth':
            # Warped intersecting shrinkage cracks, with damp crevice color.
            crack=(np.abs(np.sin(x*np.pi*32+macro*1.5))<.028)|(np.abs(np.sin(y*np.pi*30+macro*1.2))<.03)
            h-=crack*.028; shade-=crack*19
        if name=='rocky_scrub':
            stones=np.clip((aggregate-.25)*3,0,1)
            h+=stones*.075; shade+=stones*15
        if name in ('bedrock','crushed_gravel'):
            fragments=noise(n,32 if name=='bedrock' else 256,900+i)
            fissures=np.clip((.08-np.abs(fragments))*6,0,.5)
            h+=fragments*.09-fissures*.035
            shade+=fragments*18-fissures*30
        if name=='concrete_base':
            seams=((x % .25)<.0015)|((y % .25)<.0015)
            h-=seams*.022; shade-=seams*18
        c=np.array(color)[None,None,:]+shade[:,:,None]
        if name=='rocky_scrub':
            c[:,:,1]+=np.maximum(macro,0)*8
        ao=np.clip(1+np.minimum(h,0)*1.3,.85,1)
        r=np.clip(rough+macro*.025+aggregate*.03,.70,.99)
        orm=np.stack((ao,r,np.zeros_like(h)),2)*255
        save(path/f'{name}_production_base.png',c)
        save(path/f'{name}_production_normal.png',normal(h,32))
        save(path/f'{name}_production_orm.png',orm)
        # Base color stays lossless RGBA8: the CPU terrain profile also samples it.
        definition(path/f'{name}.asset.json',f'texture.world.terrain.{name}',f'{name}_production_base.png','baseColor',1024,False)
        for semantic in ('normal','orm'):
            definition(path/f'{name}_production_{semantic}.asset.json',f'texture.world.terrain.{name}_{semantic}',f'{name}_production_{semantic}.png',semantic,512,True)
        print('Authored',name,flush=True)
    mappings=dict(dirt='dry_dirt',mud='cracked_earth',grass_ground='rocky_scrub',rock='bedrock',gravel='crushed_gravel',industrial_ground='dark_ash',concrete='concrete_base',scorched='dark_ash')
    for name,family in mappings.items():
        p=ROOT/f'assets/source/world/materials/terrain/{name}.material.json'
        data=json.loads(p.read_text())
        # Reflectance is now authored in the source rather than multiplied dark twice.
        data['baseColorFactor']=[1,1,1,1]
        data['baseColorTexture']=f'texture.world.terrain.{family}'
        if name=='scorched': data['baseColorFactor']=[.65,.60,.55,1]
        data['normalTexture']=f'texture.world.terrain.{family}_normal'
        data['ormTexture']=f'texture.world.terrain.{family}_orm'
        data['roughnessFactor']=1
        data['metallicFactor']=0
        p.write_text(json.dumps(data,indent=2)+'\n')


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--family',choices=['all','atlas','terrain'],default='all')
    parser.add_argument('--terrain-name',action='append')
    args=parser.parse_args()
    if args.family in ('all','atlas'): atlas()
    if args.family in ('all','terrain'): terrain(args.terrain_name)
