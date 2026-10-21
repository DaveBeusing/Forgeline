"""Save a family-isolated master using the established preservation exporter."""
import json
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from roundtrip import run


def author(request):
    run(request)
    fixture = Path(request['arguments'][request['arguments'].index('--fixtures') + 1])
    family = json.loads(fixture.read_text(encoding='utf-8'))['families'][0]
    scene = bpy.context.scene
    for aid in sorted({obj['fl_asset'] for obj in scene.objects}):
        collection = bpy.data.collections.new(aid)
        scene.collection.children.link(collection)
        for obj in list(scene.objects):
            if obj['fl_asset'] == aid:
                for previous in list(obj.users_collection):
                    previous.objects.unlink(obj)
                collection.objects.link(obj)
                obj.hide_set(aid != family['root'])
                obj.hide_render = aid != family['root']
    scene['fl_master_root'] = family['root']
    scene['fl_surface_authority'] = 'Source material definitions; scalar preview only. Use the separate packed PBR review project.'
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(Path(request['outputRoot']) / 'authoring.blend'), check_existing=False)


if __name__ == '__main__':
    author(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
