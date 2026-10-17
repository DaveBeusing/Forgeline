"""Export a small authored rigid clip with a static containing frame through Blender's glTF exporter."""
import argparse
import json
import math
from pathlib import Path
import sys
import bpy


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--request', required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    request = json.loads(Path(args.request).read_text(encoding='utf-8-sig'))
    output = Path(request['outputRoot'])
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    pieces = []
    # Corner posts preserve the established static/culling envelope while the inner arm rotates.
    for x in (-.45, .45):
        for z in (-.45, .45):
            bpy.ops.mesh.primitive_cube_add(size=1, location=(x, z, 0))
            obj = bpy.context.object
            obj.scale = (.1, .1, 1)
            bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
            pieces.append(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in pieces:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    bpy.ops.object.join()
    frame = bpy.context.object
    frame.name = 'frame'
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.mesh.primitive_cube_add(size=1)
    arm = bpy.context.object
    arm.name = 'arm'
    arm.scale = (.4, .12, .12)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    arm.rotation_mode = 'QUATERNION'
    for frame_number, degrees, displacement in ((0, 0, 0), (24, 40, .04), (48, -20, -.02), (72, 0, 0)):
        angle = math.radians(degrees)
        arm.rotation_quaternion = (math.cos(angle/2), 0, 0, math.sin(angle/2))
        arm.location = (displacement, 0, 0)
        arm.keyframe_insert(data_path='rotation_quaternion', frame=frame_number, group='Rigid motion')
        arm.keyframe_insert(data_path='location', frame=frame_number, group='Rigid motion')
    action = arm.animation_data.action
    action.name = 'arm_cycle'
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    for point in curve.keyframe_points:
                        point.interpolation = 'LINEAR'
    surface = bpy.data.materials.new('surface')
    frame.data.materials.append(surface)
    arm.data.materials.append(surface)
    scene = bpy.context.scene
    scene.render.fps = 24
    scene.frame_start = 0
    scene.frame_end = 72
    scene.frame_set(0)
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'rigid_clip.blend'))
    bpy.ops.export_scene.gltf(filepath=str(output / 'rigid_clip.glb'), export_format='GLB',
        export_animations=True, export_animation_mode='ACTIVE_ACTIONS', export_force_sampling=False,
        export_anim_single_armature=False, export_materials='EXPORT', export_cameras=False, export_lights=False)
    samples = []
    for time in (0, .5, 1, 1.5, 2, 2.5, 3):
        scene.frame_set(int(time * 24))
        depsgraph = bpy.context.evaluated_depsgraph_get()
        evaluated = arm.evaluated_get(depsgraph)
        position, rotation, _ = evaluated.matrix_world.decompose()
        samples.append({'seconds': time, 'translation': [position.x, position.z, -position.y],
            'rotation': [rotation.x, rotation.z, -rotation.y, rotation.w]})
    (output / 'fixture-export.json').write_text(json.dumps({'blender': bpy.app.version_string,
        'samples': samples, 'clip': action.name, 'frames': [0, 24, 48, 72], 'fps': 24}, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
