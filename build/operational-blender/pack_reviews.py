"""Reopen packed inspection projects and retain source-relative image references."""
import hashlib
import json
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, fixture_selection, require, sha, write_json


def pack(request):
    catalog = Catalog(request['inputRoot'])
    reviews = Path(request['arguments'][0])
    output = Path(request['outputRoot'])
    records = []
    for family, name in ((family, family) for family in ('vehicle_factory', 'power_plant', 'barracks', 'radar')):
        ids, _ = fixture_selection(catalog, Path(__file__).with_name(family + '.json'), [])
        textures = {catalog.source(a).name: a for a in ids if catalog.definitions[a][1]['type'] == 'texture'}
        bpy.ops.wm.open_mainfile(filepath=str(reviews / name / 'surfaces.blend'))
        images = []
        for image in bpy.data.images:
            if image.source != 'FILE':
                continue
            aid = textures[Path(image.filepath).name]
            source = catalog.source(aid)
            require(image.packed_file is not None, 'Inspection texture is not packed')
            require(hashlib.sha256(bytes(image.packed_file.data)).hexdigest() == sha(source), 'Packed texture differs from source')
            expected = 'sRGB' if catalog.definitions[aid][1].get('textureColorSpace') == 'srgb' else 'Non-Color'
            require(image.colorspace_settings.name == expected, 'Texture color space changed')
            image.filepath_raw = '//../../../source/' + source.relative_to(catalog.root).as_posix()
            images.append({'id': aid, 'sourceSha256': sha(source), 'colorSpace': expected})
        require(len(images) == len(textures) == 3, 'Expected complete Base Color/Normal/ORM set')
        bpy.context.preferences.filepaths.save_version = 0
        bpy.context.scene.render.filepath = '//previews/' + family + '.png'
        target = output / (family + '_surfaces.blend')
        bpy.ops.wm.save_as_mainfile(filepath=str(target), check_existing=False, relative_remap=False)
        records.append({'family': family, 'masterSha256': sha(target), 'packedImages': images,
                        'renderPath': bpy.context.scene.render.filepath})
    write_json(output / 'packed-reviews.json', {'success': True, 'families': records})


if __name__ == '__main__':
    pack(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
