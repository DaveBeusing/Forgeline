"""Export editable Directorate combat/support component kits through shared geometry.

Kit coordinates are vehicle-local metres measured above the sampled ground.
Runtime positions are normalized by the unchanged authoritative visual scale.
Requires NumPy; geometry, materials, motion ranges and anchors remain repository-owned.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source/units/directorate'
FOLDERS = dict(scout_vehicle='vehicles', self_propelled_artillery='artillery', cargo_truck='logistics', supply_truck='logistics')
spec = importlib.util.spec_from_file_location('combat_support_geometry', Path(__file__).with_name('Author-MainBattleTank.py'))
geometry = importlib.util.module_from_spec(spec)
spec.loader.exec_module(geometry)


def component(part, lod):
    mesh = geometry.Mesh()
    if lod not in part.get('lods', [0, 1, 2]):
        return mesh
    cell, kind = part['cell'], part['kind']
    if kind == 'box':
        mesh.box(part['min'], part['max'], cell, part.get('bevel', 0) if lod == 0 else 0)
    elif kind == 'loft':
        mesh.loft(part['profiles'], cell, part.get('edge', [0, 0, 0])[lod])
    elif kind == 'cylinder':
        mesh.cylinder(part['start'], part['end'], part['radius'], cell, part['sides'][lod], part.get('inner', 0) if lod == 0 else 0)
    elif kind == 'wheel':
        mesh.wheel(part['x0'], part['x1'], part['y'], part['z'], part['radius'], part['radius'], cell, part['sides'][lod], part.get('inner', 0) if lod == 0 else 0)
    elif kind == 'belt':
        profile = [list(point) for point in part['profile']]
        if lod > 0:
            minimum = min(point[1] for point in profile)
            for point in profile:
                if point[1] == minimum: point[1] = 0
        mesh.belt(profile, part['x0'], part['x1'], part['inset'])
        mesh.tread_pads(part['profile'], part['x0'], part['x1'], part['step'][lod], part['thickness'])
    elif kind == 'panel':
        mesh.face(part['points'], cell, part['outward'])
    else:
        raise ValueError('Unknown combat/support component: ' + kind)
    return mesh


def normalize(mesh, kit):
    scale = np.asarray(kit['gameScaleMeters'], float)
    offset = np.asarray([0, kit['heightOffsetMeters'], 0], float)
    mesh.positions = [(p-offset)/scale for p in mesh.positions]
    mesh.normals = [(n*scale)/np.linalg.norm(n*scale) for n in mesh.normals]
    return mesh


def export(kit, shared, names, lod, path, articulated=False):
    mesh = geometry.Mesh()
    hierarchy, bindings, sockets = [], [], list(kit['sockets'])
    groups = {}
    for module in kit['modules']:
        if module['name'] in names:
            groups.setdefault(module.get('group', 'body'), []).append(module)
    for group, modules in groups.items():
        start = len(mesh.indices)
        for module in modules:
            first = len(mesh.indices)
            for part in module.get('parts', shared.get(module.get('source'), [])):
                mesh.merge(component(part, lod))
            if len(mesh.indices) > first:
                hierarchy.append(dict(name=module['name'], parent=module.get('parent'), sharedModule=module.get('source'), firstIndex=first, indexCount=len(mesh.indices)-first))
        if len(mesh.indices) == start:
            continue
        motion = kit['motions'].get(group)
        binding = dict(name=group, firstIndex=start, indexCount=len(mesh.indices)-start, parent=-1, motion='static')
        if articulated and motion:
            binding.update(motion=motion['kind'], socket='motion_'+group, rate=motion.get('rate', 1), travel=motion.get('travel', 0))
            pivot = np.asarray(motion['pivot'], float)
            pivot[1] -= kit['heightOffsetMeters']
            pivot /= np.asarray(kit['gameScaleMeters'], float)
            sockets.append(dict(name='motion_'+group, x=pivot[0], y=pivot[1], z=pivot[2]))
        if binding['motion'] != 'static' and bindings: binding['parent'] = 0
        bindings.append(binding)
    if not mesh.indices:
        raise ValueError('Empty component export: ' + str(path))
    normalize(mesh, kit)
    positions = np.asarray(mesh.positions)
    if not np.isfinite(positions).all():
        raise ValueError('Non-finite unit geometry')
    if np.any(positions.min(0) < np.asarray(kit['bounds']['min'])-1e-5) or np.any(positions.max(0) > np.asarray(kit['bounds']['max'])+1e-5):
        raise ValueError(f'Unit exceeds bounds: {positions.min(0)} / {positions.max(0)}')
    report = mesh.export(path)
    data = json.loads(path.read_text(encoding='utf-8'))
    data['asset']['generator'] = 'ForgeLine combat and support unit component exporter'
    data['extras'] = dict(coordinates=kit['coordinates'], componentHierarchy=hierarchy, gameScaleMeters=kit['gameScaleMeters'], heightOffsetMeters=kit['heightOffsetMeters'])
    path.write_text(json.dumps(data, indent=2)+'\n', encoding='utf-8')
    report['sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
    return report, dict(mechanicalParts=bindings if articulated else [], sockets=sockets)


def author(family, output, update):
    kit = json.loads((SOURCE/'authoring'/f'{family}.kit.json').read_text(encoding='utf-8'))
    shared = json.loads((SOURCE/'authoring/combat_support.modules.json').read_text(encoding='utf-8'))['modules']
    reports, metadata = {}, {}
    for name, names, lod, motion in [('lod0', kit['chassisModules'], 0, True), ('lod1', kit['intactModules'], 1, False), ('lod2', kit['intactModules'], 2, False)]:
        reports[name], metadata[name] = export(kit, shared, names, lod, output/f'{name}.gltf', motion)
    if kit.get('turretModules'):
        reports['turret'], metadata['turret'] = export(kit, shared, kit['turretModules'], 0, output/'turret.gltf', True)
    for state, names in kit['states'].items():
        reports[state], _ = export(kit, shared, names, 1, output/f'{state}.gltf')
    for module in kit['modules']:
        reports['kit/'+module['name']], _ = export(kit, shared, [module['name']], 0, output/'kit'/f"{module['name']}.gltf")
    (output/'exports.json').write_text(json.dumps(dict(meshes=reports, bindings=metadata), indent=2)+'\n', encoding='utf-8')
    if update:
        folder = SOURCE/FOLDERS[family]
        for lod in range(3):
            path = folder/f"{family}{'_lod'+str(lod) if lod else ''}.asset.json"
            record = json.loads(path.read_text(encoding='utf-8'))
            record['source'] = f'../sources/combat_support/{family}/lod{lod}.gltf'
            if lod == 0:
                record.update(metadata['lod0'])
            path.write_text(json.dumps(record, indent=2)+'\n', encoding='utf-8')
        for name in list(kit['states']) + (['turret'] if kit.get('turretModules') else []):
            record = dict(id=f'unit.directorate.{family}.{name}', type='mesh', source=f'../sources/combat_support/{family}/{name}.gltf', materialReferences=[f'material.directorate.unit.{family}'])
            if name == 'turret':
                record.update(metadata[name])
            (folder/f'{family}_{name}.asset.json').write_text(json.dumps(record, indent=2)+'\n', encoding='utf-8')
    print(family, json.dumps({n:r['triangles'] for n,r in reports.items() if not n.startswith('kit/')}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--family', choices=FOLDERS, action='append')
    parser.add_argument('--output', type=Path, default=SOURCE/'sources/combat_support')
    parser.add_argument('--update-metadata', action='store_true')
    args = parser.parse_args()
    for family in args.family or FOLDERS:
        author(family, args.output/family, args.update_metadata)
