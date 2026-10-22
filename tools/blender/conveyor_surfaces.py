"""Deterministic conveyor atlas authoring. Run with the pinned Blender Python.

One shared opaque material, no shader changes. RGB wear masks are authoring
inputs baked into BaseColor/Normal/ORM, not unsupported runtime mask bindings.
"""
import json
import sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from pbr_png import write_png
from conveyor_models import ROOT, STATUS, write_json

SIZE = 2048
CELL = SIZE // 4
MATERIAL = 'material.infrastructure.directorate.conveyor'
FOLDER = ROOT / 'assets/source/infrastructure/directorate/conveyors/materials'
# RGB below is display/sRGB; roughness/metallic and masks are linear.
CELLS = [
    ('painted_frame', (82, 91, 64), .72, 0),
    ('structural_steel', (76, 82, 84), .56, .88),
    ('belt_composite', (30, 33, 31), .94, 0),
    ('roller_rubber', (40, 43, 41), .94, 0),
    ('flow_indicator', (34, 40, 36), .78, 0),
    ('hazard_paint', (183, 132, 46), .78, 0),
    ('concrete', (126, 126, 112), .91, 0),
    ('roller_steel', (94, 100, 101), .65, .85),
    ('worn_paint', (82, 91, 64), .72, 0),
    ('offline_indicator', (42, 46, 43), .82, 0),
    ('cargo_pack', (104, 95, 66), .88, 0),
    ('critical_scorch', (55, 54, 43), .83, 0),
    ('idle_indicator', (43, 48, 42), .78, 0),
    ('blocked_indicator', (48, 43, 33), .78, 0),
    ('directorate_service', (82, 91, 64), .72, 0),
    ('disconnected_indicator', (43, 45, 40), .78, 0),
]


def linear(rgb):
    c = np.asarray(rgb, dtype=np.float32) / 255
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= .0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - .055)


def author():
    FOLDER.mkdir(parents=True, exist_ok=True)
    maps = {key: np.zeros((SIZE, SIZE, 4), np.uint8)
            for key in ('base', 'normal', 'orm', 'emissive', 'wear')}
    for pixels in maps.values():
        pixels[:, :, 3] = 255
    y, x = np.mgrid[0:CELL, 0:CELL].astype(np.float32) / (CELL - 1)
    # Broad low-frequency fields, no random one-pixel salt/pepper or baked light.
    field = .5 + .23 * np.sin(x * 19 + np.sin(y * 13)) + .17 * np.cos(y * 23 - x * 7)
    edge = np.minimum.reduce([x, y, 1-x, 1-y])
    for i, (name, color, roughness, metallic) in enumerate(CELLS):
        dirt = np.clip((field - .54) * 1.3, 0, .38)
        wear = np.clip((.14 - edge) * 5, 0, .6) * np.clip((field-.3)*2, 0, 1)
        heat = np.zeros_like(x)
        crack = np.interp(y, [0,.12,.25,.34,.45,.58,.67,.8,1],
                          [.46,.46,.53,.43,.50,.44,.52,.47,.47])
        fissure = (np.abs(x-crack) < .009+.012*np.sin(y*np.pi)**2) & (y>.12) & (y<.86)
        branch = (np.abs(y-(.36+(x-.45)*.9)) < .012) & (x>.23) & (x<.46)
        if i == 8:
            wear = np.maximum(wear, (fissure | branch) * .95)
        if i == 11:
            heat = np.clip(1 - np.hypot(x-.55, y-.5)*2.1, 0, 1)
            fracture = (np.abs(x-crack)<.03) & (y>.08) & (y<.92)
            wear = np.maximum(wear, (fracture | branch) * .95)
        # Dielectric paint chips expose steel; dirt/scorch cover that substrate.
        chip = wear if i in (0, 8, 11, 14) else np.zeros_like(x)
        base = linear(color)[None, None, :] * (1 - .22*dirt[:, :, None])
        base = base*(1-chip[:, :, None]) + linear((95, 98, 94))*chip[:, :, None]
        base *= 1 - .72*heat[:, :, None]
        rough = np.clip(roughness - .16*chip + .14*dirt + .12*heat, .45, .98)
        metal = np.clip(metallic*(1-.7*dirt) + .85*chip, 0, 1) * (1-.85*heat)
        emit = np.zeros((CELL, CELL, 3), np.float32)
        glyph = np.zeros_like(x, dtype=bool)
        if i == 5:
            # Four broad diagonal bands per chart; black/yellow pattern survives mips.
            stripe = ((x+y)*3 % 1) < .48
            base[stripe] = linear((37, 39, 34))
        if i == 2:
            groove = (np.abs(y-.32) < .018) | (np.abs(y-.68) < .018)
            base[groove] *= .75
        if i == 4:
            glyph = (np.abs(y-.5) < .18) & ((np.abs(x-.35) < .065) | (np.abs(x-.65) < .065))
        if i == 12:
            radius = np.hypot(x-.5, y-.5)
            glyph = (radius > .18) & (radius < .27)
        if i == 13:
            glyph = (np.minimum(np.abs(x-y), np.abs(x+y-1)) < .06) & (edge > .22)
        if i == 9:
            radius = np.hypot(x-.5, y-.53)
            glyph = ((radius > .20) & (radius < .26) & (y > .39)) | ((np.abs(x-.5) < .04) & (y > .2) & (y < .58))
            glyph |= (np.abs(x+y-1) < .035) & (edge > .15)
        if i == 15:
            glyph = (np.abs(y-.5) < .06) & (((x > .20) & (x < .43)) | ((x > .57) & (x < .80)))
            glyph |= ((np.abs(x-.24)<.04) | (np.abs(x-.76)<.04)) & (np.abs(y-.5)<.19)
        if i == 14:
            # Directorate rank bars and service wrench-like open jaw, not tiny text.
            glyph = (np.abs(x-.5)<.24) & ((np.abs(y-.35)<.045) | (np.abs(y-.5)<.045) | (np.abs(y-.65)<.045))
        if glyph.any():
            ink = (153, 173, 124) if i == 4 else ((155, 157, 144) if i in (9,12,14,15) else (205,151,55))
            base[glyph] = linear(ink)
            rough[glyph] = .78
            metal[glyph] = 0
            # Only a tiny corner pilot is luminous. Printed glyph remains unlit.
            if i in (4, 13):
                pilot = (np.abs(x-.78)<.025) & (np.abs(y-.22)<.025)
                emit[pilot] = linear((100, 137, 67) if i == 4 else (170, 101, 22))
        height = .0015 * np.sin(x*22) * np.cos(y*17) + .006*chip - .004*heat
        if i == 2:
            height += .002*np.sin(y*22)
        # Tangent +Y corresponds to increasing Blender V, opposite PNG row.
        dy, dx = np.gradient(height)
        n = np.stack((-dx*CELL, dy*CELL, np.ones_like(x)), axis=2)
        n /= np.linalg.norm(n, axis=2, keepdims=True)
        result = {'base': srgb(base), 'normal': n*.5+.5,
                  'orm': np.stack((np.ones_like(x), rough, metal), axis=2),
                  'emissive': srgb(emit), 'wear': np.stack((dirt, wear, heat), axis=2)}
        # Extend edges through a 32px guard; UVs use 36.864px inset at 2048.
        indices = np.clip(np.arange(CELL), 32, CELL-33)
        for kind, values in result.items():
            block = np.rint(np.clip(values,0,1)*255).astype(np.uint8)
            block = block[indices[:, None], indices[None, :]]
            row, col = divmod(i, 4)
            maps[kind][row*CELL:(row+1)*CELL,col*CELL:(col+1)*CELL,:3] = block
    for kind, pixels in maps.items():
        write_png(FOLDER / ('conveyor_'+kind+'.png'), SIZE, SIZE, pixels.tobytes())
    for kind, usage, dimension, levels in [('base','baseColor',2048,5), ('normal','normal',1024,4), ('orm','orm',512,3), ('emissive','emissive',512,3)]:
        write_json(FOLDER / (kind+'.asset.json'), {
            'id': 'texture.infrastructure.directorate.conveyor.'+kind, 'type':'texture',
            'source':'conveyor_'+kind+'.png', 'textureUsage':usage,
            'textureColorSpace':'srgb' if kind in ('base','emissive') else 'linear',
            'textureGenerateMipmaps':True, 'textureMaxDimension':dimension,
            'textureFormat':'bc7Unorm', 'textureMaxMipLevels':levels,
            'textureCompressionQuality':'balanced'})
    write_json(FOLDER/'conveyor.asset.json', {'id':MATERIAL,'type':'material','source':'conveyor.material.json'})
    write_json(FOLDER/'conveyor.material.json', {
        'baseColorFactor':[1,1,1,1], 'metallicFactor':1, 'roughnessFactor':1,
        **{key+'Texture':'texture.infrastructure.directorate.conveyor.'+value for key,value in [('baseColor','base'),('normal','normal'),('orm','orm'),('emissive','emissive')]},
        'emissiveMultiplier':.35, 'uvScale':[1,1], 'uvOffset':[0,0]})
    write_json(FOLDER/'recipe.json', {'schema':'forgeline.conveyor_surfaces','schemaVersion':1,
        'generator':'tools/blender/conveyor_surfaces.py','sourceSize':SIZE,'grid':[4,4],
        'cells':[{'index':i,'name':v[0],'baseSRGB8':v[1],'roughness':v[2],'metallic':v[3]} for i,v in enumerate(CELLS)],
        'wearMaskChannels':{'R':'dirt','G':'substrate exposure / chips','B':'heat / soot'},
        'maskPolicy':'source-only; baked into PBR maps, no runtime shader mask parameter',
        'statusCells':STATUS,'emissiveMultiplier':.35,'alpha':'opaque',
        'normal':'tangent +Y, generated from common height field; no baked illumination',
        'testsExecuted':False,'manualAcceptance':'pending'})


if __name__ == '__main__':
    author()
