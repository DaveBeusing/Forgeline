"""Eight terrain slots, seven shared source sets, unchanged compiler policy."""
import copy
import hashlib
import json
import math
from pathlib import Path

SLOTS = dict(grass_ground='rocky_scrub', dirt='dry_dirt', mud='cracked_earth',
             rock='bedrock', gravel='crushed_gravel', industrial_ground='dark_ash',
             concrete='concrete_base', scorched='dark_ash')
FAMILIES = tuple(dict.fromkeys(SLOTS.values()))
MAPS = dict(base=('baseColor', 'srgb', 1024), normal=('normal', 'linear', 512),
            orm=('orm', 'linear', 512))
TEXTURES = Path('world/textures/terrain')
MATERIALS = Path('world/materials/terrain')
TILES = dict(rocky_scrub=32, dry_dirt=32, cracked_earth=16, bedrock=32,
             crushed_gravel=12, dark_ash=32/1.5, concrete_base=16)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def definition_name(family, kind):
    require(family in FAMILIES and kind in MAPS, 'Unknown terrain family/channel')
    return f'{family}.asset.json' if kind == 'base' else f'{family}_production_{kind}.asset.json'


def binding(root, family):
    root = Path(root)
    require(family in FAMILIES, 'Unknown terrain family')
    result = {'schemaVersion': 1, 'family': family, 'tileMetres': TILES[family],
              'maps': {}, 'materials': {}}
    for kind, (usage, space, cap) in MAPS.items():
        path = root / TEXTURES / definition_name(family, kind)
        d = read(path)
        require(set(d) == {'id','type','source','textureUsage','textureColorSpace','textureMaxDimension',
                           'textureGenerateMipmaps','textureFormat','textureCompressionQuality'},
                'Unknown/missing terrain texture field')
        require(d['source'] in (f'{family}_production_{kind}.png', f'blender/{family}/{family}_production_{kind}.png'),
                'Unexpected/unsafe terrain texture source')
        expected = f'texture.world.terrain.{family}' + ('' if kind == 'base' else '_'+kind)
        require(d['id'] == expected and d['type'] == 'texture', 'Stable texture identity changed')
        require((d['textureUsage'], d['textureColorSpace'], d['textureMaxDimension']) ==
                (usage, space, cap), 'Terrain semantic/color space/cap changed')
        require(d['textureGenerateMipmaps'] is True and 'textureMaxMipLevels' not in d and
                d['textureFormat'] == 'bc7Unorm' and d['textureCompressionQuality'] == 'balanced',
                'Terrain format/full mip/compression contract changed')
        original = root / TEXTURES / f'{family}_production_{kind}.png'
        h = original.read_bytes()[:29]
        require(h[:8] == b'\x89PNG\r\n\x1a\n' and
                int.from_bytes(h[16:20], 'big') == int.from_bytes(h[20:24], 'big') == 4096 and
                h[24] == 8 and h[25] in (2, 6), 'Original terrain master must be 4K RGB(A)8')
        policy = copy.deepcopy(d)
        policy.pop('source')
        result['maps'][kind] = {'definition': path.relative_to(root).as_posix(),
                               'policy': policy, 'original': original.relative_to(root).as_posix(),
                               'originalSha256': digest(original)}
    for slot, source in SLOTS.items():
        if source != family:
            continue
        material = read(root / MATERIALS / f'{slot}.material.json')
        for key, kind in [('baseColorTexture', 'base'), ('normalTexture', 'normal'), ('ormTexture', 'orm')]:
            require(material[key] == result['maps'][kind]['policy']['id'], 'Shared terrain slot mapping changed')
        result['materials'][slot] = material
    return result


def controls(tint, roughness_bias, detail):
    require(len(tint) == 4 and tint[3] == 1 and all(math.isfinite(x) and .8 <= x <= 1.2 for x in tint[:3]),
            'Linear tint must be bounded to 0.8..1.2 with opaque alpha')
    require(math.isfinite(roughness_bias) and -.05 <= roughness_bias <= .05, 'Roughness bias exceeds 0.05')
    require(math.isfinite(detail) and 0 <= detail <= .04, 'Periodic detail exceeds 0.04')
