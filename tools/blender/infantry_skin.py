"""Rig existing Directorate figure geometry and export editable, independently authored skeletal clips."""
import argparse
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Quaternion, Vector
sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import read_geometry, game_to_blender, blender_to_game
from infantry_surface import blender_uv, packed_surface

ACTIONS = ('idle','walk','run','aim','fire','reload','hit','suppression','death')
BONES = ('pelvis','spine','head','upperarm_left','forearm_left','upperarm_right','forearm_right','thigh_left','shin_left','thigh_right','shin_right')


def build_family(source, output, family):
    definition = json.loads((source / f'units/directorate/infantry/{family}.asset.json').read_text(encoding='utf-8-sig'))
    # The preserved static source is the bind geometry owner during migration.
    geometry_path = source / f'units/directorate/sources/infantry/{family}_lod0.gltf'
    geometry = read_geometry(geometry_path, source, definition)
    recipe = json.loads((source / 'units/directorate/authoring/infantry.kit.json').read_text())['families'][family]
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    bpy.data.orphans_purge(do_recursive=True)
    positions = []; triangles = []; uv = []; normals = []
    for primitive in geometry['primitives']:
        attrs = primitive['attributes']; start = len(positions)
        positions.extend(attrs['POSITION']); uv.extend(attrs['TEXCOORD_0']); normals.extend(attrs['NORMAL'])
        triangles.extend(tuple(start + v for v in primitive['indices'][i:i+3]) for i in range(0,len(primitive['indices']),3))
    mesh = bpy.data.meshes.new('SquadGeometry'); mesh.from_pydata([game_to_blender(p) for p in positions], [], triangles); mesh.update()
    obj = bpy.data.objects.new('Squad', mesh); bpy.context.collection.objects.link(obj)
    uv_layer = mesh.uv_layers.new(name='UVMap')
    for loop in mesh.loops: uv_layer.data[loop.index].uv = blender_uv(uv[loop.vertex_index])
    mesh.normals_split_custom_set([game_to_blender(normals[loop.vertex_index]) for loop in mesh.loops])
    material = bpy.data.materials.new('Directorate infantry'); mesh.materials.append(material)
    packed_surface(material, source)
    armature = bpy.data.armatures.new('InfantryRig'); rig = bpy.data.objects.new('InfantryRig', armature); bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig; rig.select_set(True); obj.select_set(False)
    bpy.ops.object.mode_set(mode='EDIT')
    sockets = {s['name']:s for s in recipe['sockets']}
    origins = []
    for member in range(4):
        shoulder = sockets[f'member_{member}_shoulder']; x,z = shoulder['x'],shoulder['z']; origins.append((x,z))
        points = {
            'pelvis':((x,.015,z),(x,.09,z),None),
            'spine':((x,.09,z),(x,.25,z),'pelvis'),
            'head':((x,.25,z),(x,.43,z),'spine'),
            'upperarm_left':((x-.09,.24,z),(x-.11,.10,z+.10),'spine'),
            'forearm_left':((x-.11,.10,z+.10),(x-.05,.05,z+.22),'upperarm_left'),
            'upperarm_right':((x+.09,.24,z),(x+.11,.10,z+.10),'spine'),
            'forearm_right':((x+.11,.10,z+.10),(x+.05,.05,z+.23),'upperarm_right'),
            'thigh_left':((x-.043,.015,z),(x-.043,-.22,z),'pelvis'),
            'shin_left':((x-.043,-.22,z),(x-.043,-.48,z+.03),'thigh_left'),
            'thigh_right':((x+.043,.015,z),(x+.043,-.22,z),'pelvis'),
            'shin_right':((x+.043,-.22,z),(x+.043,-.48,z+.03),'thigh_right')}
        for name in BONES:
            head,tail,parent = points[name]; bone = armature.edit_bones.new(f'member_{member}_{name}')
            bone.head = game_to_blender(head); bone.tail = game_to_blender(tail)
            if parent: bone.parent = armature.edit_bones[f'member_{member}_{parent}']
    bpy.ops.object.mode_set(mode='OBJECT')
    for bone in rig.pose.bones: bone.rotation_mode = 'QUATERNION'
    for bone in armature.bones: obj.vertex_groups.new(name=bone.name)
    # Existing module triangle ownership assigns equipment to its anatomical bone, then blends elbow/knee seams.
    primitive = geometry['primitives'][0]; ownership = {}
    source_ranges = json.loads(geometry_path.read_text())['extras']['componentHierarchy']
    for part in source_ranges:
        for k in range(part['firstIndex'], part['firstIndex']+part['indexCount']): ownership[primitive['indices'][k]] = (part['module'],part['name'])
    for i,p in enumerate(positions):
        member = min(range(4),key=lambda m:(p[0]-origins[m][0])**2+(p[2]-origins[m][1])**2)
        x,z = origins[member]; name,component = ownership.get(i,('body','body'))
        if 'hip_left' in name: bone = 'thigh_left' if p[1] > -.22 else 'shin_left'
        elif 'hip_right' in name: bone = 'thigh_right' if p[1] > -.22 else 'shin_right'
        elif 'shoulder' in name:
            # Weapon, hands and forearms remain attached to the same right-hand chain.
            bone = {'upper_arm_left':'upperarm_left','forearm_left':'forearm_left','hand_left':'forearm_left',
                    'upper_arm_right':'upperarm_right'}.get(component,'forearm_right')
        else: bone = 'head' if p[1] > .28 else 'pelvis' if p[1] < .09 else 'spine'
        obj.vertex_groups[f'member_{member}_{bone}'].add([i],1,'REPLACE')
        if bone.startswith('shin') and abs(p[1]+.22) < .025:
            parent = bone.replace('shin','thigh'); obj.vertex_groups[f'member_{member}_{bone}'].add([i],.7,'REPLACE'); obj.vertex_groups[f'member_{member}_{parent}'].add([i],.3,'REPLACE')
    modifier = obj.modifiers.new('Infantry skin','ARMATURE'); modifier.object = rig
    scene = bpy.context.scene; scene.render.fps = 24; scene.frame_start = 0; scene.frame_end = 24
    # Keys are pose-bone local rotations; glTF exports absolute joint-local TRS with matching inverse binds.
    for action in ACTIONS:
        rig.animation_data_create()
        rig.animation_data.action = None
        for track in rig.animation_data.nla_tracks: track.mute = True
        for bone in rig.pose.bones: bone.rotation_quaternion = Quaternion(); bone.location = (0,0,0)
        for frame in range(0,25,3):
            time = frame/24; wave = math.sin(math.tau*time)
            for member in range(4):
                phase = math.sin(math.tau*time + member*.55)
                for bone_name in BONES:
                    bone = rig.pose.bones[f'member_{member}_{bone_name}']; angle = 0.; axis = 'X'; displacement = 0.
                    if action in ('walk','run'):
                        gain = .35 if action == 'walk' else .6
                        if bone_name.startswith('thigh'): angle = phase*gain*(1 if bone_name.endswith('left') else -1)
                        if bone_name.startswith('shin'): angle = max(0,phase*(1 if bone_name.endswith('left') else -1))*gain
                        if bone_name == 'pelvis': displacement = abs(phase)*(.012 if action == 'walk' else .025)
                    elif action == 'idle' and bone_name == 'spine': angle = wave*.018
                    elif action == 'aim' and bone_name == 'spine': angle = -.045 + wave*.006
                    elif action == 'fire' and bone_name in ('spine','upperarm_right'): angle = -.11*math.sin(math.pi*min(1,time*3)) if time < 1/3 else 0
                    elif action == 'reload' and bone_name == 'forearm_left': angle = .8*math.sin(math.pi*time)
                    elif action == 'hit' and bone_name == 'spine': angle = .2*math.sin(math.pi*time)
                    elif action == 'suppression' and bone_name == 'spine': angle = -.16 + wave*.01
                    elif action == 'death' and bone_name == 'pelvis': angle = min(1,time*1.5)*1.2
                    bone.rotation_quaternion = Quaternion((1,0,0),angle); bone.location = (0,displacement,0)
                    bone.keyframe_insert('rotation_quaternion',frame=frame); bone.keyframe_insert('location',frame=frame)
        authored = rig.animation_data.action; authored.name = action
        for layer in authored.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        for point in curve.keyframe_points: point.interpolation = 'LINEAR'
        track = rig.animation_data.nla_tracks.new(); track.name = action
        strip = track.strips.new(action,0,authored); strip.name = action
        rig.animation_data.action = None
    for track in rig.animation_data.nla_tracks: track.mute = False
    rig.animation_data.use_nla = False
    scene.frame_set(0)
    rig.select_set(True); obj.select_set(True); bpy.context.view_layer.objects.active = rig
    bpy.ops.wm.save_as_mainfile(filepath=str(output/f'{family}.blend'))
    export_rig(output/f'{family}.glb')
    return {'family':family,'bones':len(armature.bones),'vertices':len(positions),'triangles':len(triangles),'actions':list(ACTIONS),'sockets':recipe['sockets']}


def export_rig(path):
    # Native preview graphs stay in the editable master. Runtime material IDs
    # resolve the existing compiled atlas; embedding it in every skin exceeds the
    # bounded skin source size and duplicates authoritative texture ownership.
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,
        export_animations=True,export_animation_mode='NLA_TRACKS',export_force_sampling=True,export_frame_range=True,
        export_skins=True,export_all_influences=False,export_tangents=True,export_materials='PLACEHOLDER',export_cameras=False,export_lights=False)


def reopen_family(source, output, family):
    master = source / f'units/directorate/authoring/infantry/{family}.blend'
    bpy.ops.wm.open_mainfile(filepath=str(master), load_ui=False, use_scripts=False)
    rig = bpy.data.objects['InfantryRig']; obj = bpy.data.objects['Squad']
    if len(rig.data.bones) != 44: raise ValueError('Unexpected infantry rig')
    samples = []
    tracks = rig.animation_data.nla_tracks
    if {track.name for track in tracks} != set(ACTIONS): raise ValueError('Incomplete action library')
    for action_name in ACTIONS:
        action = next(track.strips[0].action for track in tracks if track.name == action_name)
        rig.animation_data.use_nla = False; rig.animation_data.action = action
        rig.animation_data.action_slot = action.slots[0]
        for frame in (0,9,18,24):
            bpy.context.scene.frame_set(frame); bpy.context.view_layer.update()
            for bone in rig.pose.bones:
                samples.append(dict(action=action_name,seconds=frame/24,joint=bone.name,
                    bind=list(blender_to_game(rig.data.bones[bone.name].matrix_local.translation)),
                    position=list(blender_to_game(bone.matrix.translation))))
    rig.animation_data.action = None; rig.animation_data.use_nla = False
    bpy.context.scene.frame_set(0); bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True); obj.select_set(True); bpy.context.view_layer.objects.active=rig
    export_rig(output/f'{family}.glb')
    # Demonstrate that the saved master remains editable, export the changed copy separately.
    action = tracks[0].strips[0].action
    curve = next(curve for layer in action.layers for strip in layer.strips for bag in strip.channelbags for curve in bag.fcurves if curve.data_path.endswith('rotation_quaternion') and curve.array_index == 1)
    original = curve.keyframe_points[1].co.y; curve.keyframe_points[1].co.y = original + .002
    export_rig(output/f'{family}-edited.glb')
    curve.keyframe_points[1].co.y = original
    return dict(family=family,bones=44,actions=list(ACTIONS),samples=samples)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--request',required=True)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]);request=json.loads(Path(args.request).read_text(encoding='utf-8-sig'))
    source=Path(request['inputRoot']);output=Path(request['outputRoot'])
    operation = reopen_family if '--reopen' in request.get('arguments',[]) else build_family
    reports=[operation(source,output,family) for family in ('rifle_squad','combat_engineer')]
    (output/'rig-report.json').write_text(json.dumps({'blender':bpy.app.version_string,'families':reports},indent=2)+'\n',encoding='utf-8')

if __name__ == '__main__': main()
