"""Preserve one integrated prop family through the bounded Blender adapter."""
import argparse
import json
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from prop_contract import PropCatalog as Catalog
from asset_contract import fixture_selection, read_geometry, require, sha, write_json
import roundtrip
roundtrip.Catalog = Catalog
import production


def run(request):
    parser = argparse.ArgumentParser()
    parser.add_argument('--fixtures', required=True, type=Path)
    parser.add_argument('--family', required=True)
    parser.add_argument('--blend-file', type=Path)
    parser.add_argument('--inspect', action='store_true')
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args(request['arguments'])
    root = 'mesh.world.prop.' + args.family
    forwarded = ['--fixtures', str(args.fixtures), '--asset-id', root]
    if args.blend_file:
        forwarded += ['--action', 'Export', '--blend-file', str(args.blend_file)]
    roundtrip.run(dict(request, arguments=forwarded))
    if not (args.inspect or args.render):
        return
    output = Path(request['outputRoot'])
    catalog = Catalog(request['inputRoot'])
    ids, families = fixture_selection(catalog, args.fixtures, [root])
    geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
                  for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
    # Textured inspection is separate from the strictly exportable rest master.
    roundtrip.import_scene(catalog, ids, families, geometries)
    costs = production.surface_preview(catalog, ids)
    captures = production.previews(families, geometries, output, args.render)
    for aid in geometries:
        layer = bpy.data.collections.new(aid)
        bpy.context.scene.collection.children.link(layer)
        for obj in list(bpy.context.scene.objects):
            if obj.get('fl_asset') != aid:
                continue
            for collection in list(obj.users_collection):
                collection.objects.unlink(obj)
            layer.objects.link(obj)
            obj.hide_set(aid != root)
            obj.hide_render = aid != root
    packed = []
    for image in bpy.data.images:
        if image.packed_file:
            source = Path(bpy.path.abspath(image.filepath)).resolve()
            require(bytes(image.packed_file.data) == source.read_bytes(), 'Packed texture differs from source')
            packed.append({'source': source.relative_to(catalog.root).as_posix(),
                           'sha256': sha(source), 'colorSpace': image.colorspace_settings.name})
            image.filepath = '//textures/' + source.name
    bpy.context.scene.render.filepath = '//previews/' + args.family + '.png'
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'inspection.blend'), check_existing=False)
    write_json(output / 'inspection.json', {'schemaVersion': 1, 'success': True,
               'family': root, 'packedImages': packed, 'textures': costs, 'previews': captures,
               'sourceSignature': catalog.signature(ids)[0], 'humanArtApproval': 'pending'})


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
