"""Correct saved infantry UV orientation without reconstructing geometry or rigs."""
import hashlib
import json
from pathlib import Path
import sys
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import read_geometry, require, sha
from infantry_skin import ACTIONS, export_rig
from infantry_surface import blender_uv, packed_surface


def preserved_state(obj, rig):
    actions = []
    for track in rig.animation_data.nla_tracks:
        require(len(track.strips) == 1, 'Unexpected NLA strip count')
        action = track.strips[0].action
        curves = [(curve.data_path, curve.array_index,
                   [(list(p.co), p.interpolation) for p in curve.keyframe_points])
                  for layer in action.layers for strip in layer.strips
                  for bag in strip.channelbags for curve in bag.fcurves]
        actions.append((track.name, action.name, curves))
    require(len(rig.data.bones) == 44 and {a[0] for a in actions} == set(ACTIONS), 'Rig/action contract changed')
    state = dict(vertices=[list(v.co) for v in obj.data.vertices],
                 triangles=[list(p.vertices) for p in obj.data.polygons],
                 weights=[[(g.group, g.weight) for g in v.groups] for v in obj.data.vertices],
                 groups=[g.name for g in obj.vertex_groups],
                 bones=[(b.name, b.parent.name if b.parent else None,
                         [list(row) for row in b.matrix_local]) for b in rig.data.bones],
                 actions=actions)
    return hashlib.sha256(json.dumps(state, sort_keys=True, allow_nan=False).encode()).hexdigest()


def main(request):
    source, output = Path(request['inputRoot']), Path(request['outputRoot'])
    records = []
    for family in ('rifle_squad', 'combat_engineer'):
        master = source / f'units/directorate/authoring/infantry/{family}.blend'
        bpy.ops.wm.open_mainfile(filepath=str(master), load_ui=False, use_scripts=False)
        obj, rig = bpy.data.objects['Squad'], bpy.data.objects['InfantryRig']
        before = preserved_state(obj, rig)
        definition = json.loads((source / f'units/directorate/infantry/{family}.asset.json').read_text(encoding='utf-8-sig'))
        geometry = read_geometry(source / f'units/directorate/sources/infantry/{family}_lod0.gltf', source, definition)
        uv = [v for p in geometry['primitives'] for v in p['attributes']['TEXCOORD_0']]
        require(len(uv) == len(obj.data.vertices), 'Static/source vertex correspondence changed')
        layer = obj.data.uv_layers.active
        require(layer is not None, 'Missing infantry UVs')
        old_error = max(abs(layer.data[l.index].uv[k] - uv[l.vertex_index][k])
                        for l in obj.data.loops for k in (0, 1))
        new_error = max(abs(layer.data[l.index].uv[k] - blender_uv(uv[l.vertex_index])[k])
                        for l in obj.data.loops for k in (0, 1))
        require(min(old_error, new_error) < 1e-6, 'Native UVs differ from both known conventions; review before editing')
        if new_error >= 1e-6:
            for loop in obj.data.loops:
                layer.data[loop.index].uv = blender_uv(uv[loop.vertex_index])
        output_error = max(abs(layer.data[l.index].uv[k] - blender_uv(uv[l.vertex_index])[k])
                           for l in obj.data.loops for k in (0, 1))
        require(output_error < 1e-6, 'Corrected native UVs differ from source convention')
        packed_surface(obj.data.materials[0], source)
        obj['atlas_uv_origin'] = 'glTF-top-left; Blender V=1-glTF V'
        require(before == preserved_state(obj, rig), 'Geometry, weights, joints or actions changed')
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active = rig
        rig.animation_data.action = None; rig.animation_data.use_nla = False
        bpy.context.scene.frame_set(0)
        export_rig(output / f'{family}.glb')
        packed = [dict(name=i.name, sha256=hashlib.sha256(bytes(i.packed_file.data)).hexdigest(),
                       colorSpace=i.colorspace_settings.name, path=i.filepath)
                  for i in bpy.data.images if i.packed_file]
        require(len(packed) == 3, f'Expected exactly three packed atlas maps: {packed}')
        # Paths target the canonical master location, not this isolated stage.
        bpy.ops.wm.save_as_mainfile(filepath=str(output / f'{family}.blend'), check_existing=False, relative_remap=False)
        records.append(dict(family=family, originalMasterSha256=sha(master),
                            preservedStateSha256=before, packedImages=packed,
                            inputErrorToUnconvertedUvs=old_error, inputErrorToConvertedUvs=new_error,
                            correctedUvError=output_error,
                            exportSha256=sha(output / f'{family}.glb')))
    (output / 'surface-report.json').write_text(json.dumps(dict(blender=bpy.app.version_string,
        success=True, families=records), indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
