"""Family-local source, geometry and surface limits for the Command Core."""
import copy
import json
import math
import struct
import zlib
from pathlib import Path

from asset_contract import compare_geometry, geometry_stats, require

ROOT = 'building.directorate.command_core'
MATERIAL = 'material.directorate.building.command_core'
TEXTURE = 'texture.directorate.building.command_core.emissive'
SUFFIXES = ('', '.lod1', '.lod2', '.foundation', '.structural_frame', '.partial_shell',
            '.unpowered', '.damaged', '.critical', '.destroyed')
TRIANGLES = dict(zip(SUFFIXES, (4244, 952, 348, 108, 240, 288, 36, 48, 180, 204)))
GEOMETRY_BYTES = {'': 479856, '.lod1': 101664, '.lod2': 38160}
MINIMUM = (-.505, -.5, -.34)
MAXIMUM = (.505, .56, .48)


def material_for(suffix):
    if suffix in ('', '.lod1', '.lod2'):
        return MATERIAL
    if suffix in ('.unpowered', '.damaged', '.critical', '.destroyed'):
        return MATERIAL + suffix
    return None


def bindings(catalog, ids):
    result = {}
    for aid in ids:
        definition = copy.deepcopy(catalog.definitions[aid][1])
        definition.pop('source', None)
        if aid.startswith(ROOT) and material_for(aid[len(ROOT):]):
            definition['materialReferences'] = [material_for(aid[len(ROOT):])]
        result[aid] = definition
    return result


def compare(aid, before, after, definition):
    if aid != ROOT and not aid.startswith(ROOT + '.'):
        return compare_geometry(before, after)
    suffix = aid[len(ROOT):]
    require(suffix in TRIANGLES, 'Unknown Command Core state')
    stats = geometry_stats(after)
    require(stats['triangles'] <= TRIANGLES[suffix] and stats['primitives'] == 1,
            'Command Core triangle/section budget exceeded')
    size = stats['vertices'] * 48 + stats['triangles'] * 12
    if suffix in GEOMETRY_BYTES:
        require(size <= GEOMETRY_BYTES[suffix], 'Command Core geometry memory budget exceeded')
        require(all(abs(stats[key][axis] - target[axis]) <= 1e-5
                    for key, target in (('boundsMin', MINIMUM), ('boundsMax', MAXIMUM))
                    for axis in range(3)), 'Command Core silhouette bounds changed')
    rest = copy.deepcopy(before)
    maximum = 0.0
    for old, new in zip(rest['primitives'], after['primitives']):
        positions = new['attributes']['POSITION']
        require(len(positions) == len(old['attributes']['POSITION']), 'Command Core topology changed')
        for original, point in zip(old['attributes']['POSITION'], positions):
            require(all(math.isfinite(v) for v in point), 'Nonfinite Command Core position')
            require(all(MINIMUM[i] - 1e-5 <= point[i] <= MAXIMUM[i] + 1e-5 for i in range(3)),
                    'Command Core geometry escapes retained bounds')
            maximum = max(maximum, max(abs(a - b) for a, b in zip(original, point)))
        old['attributes']['POSITION'] = positions
        for start in range(0, len(new['indices']), 3):
            a, b, c = (positions[index] for index in new['indices'][start:start + 3])
            u, v = ([b[i] - a[i] for i in range(3)], [c[i] - a[i] for i in range(3)])
            normal = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
            length = math.sqrt(sum(x*x for x in normal))
            require(length > 1e-9, 'Degenerate Command Core face')
            expected = new['attributes']['NORMAL'][new['indices'][start]]
            require(sum(x*y for x, y in zip(normal, expected))/length > .9999,
                    'Command Core edit no longer matches its authored normal')
    result = compare_geometry(rest, after)
    result['maximumAuthoredPositionEdit'] = maximum
    return result


def refined_kit(original):
    """Explicit bootstrap recipe; never applied when reopening an authored master."""
    kit = copy.deepcopy(original)
    groups = {group['name']: group for group in kit['modules']}
    protected = {'name': 'protected_signal_spine', 'parts': []}
    piping = {'name': 'service_piping', 'parts': []}
    for group in kit['modules']:
        retained = []
        for part in group['parts']:
            name = part['name']
            if not name.startswith(('armoured_wing', 'overhanging_roof', 'rear_command_bridge',
                                    'communications_spine', 'front_armour_cheek')):
                part['bevel'] = 0
            if name.startswith('armoured_wing'):
                part['cell'] = 6
            if name == 'recessed_window_strip':
                part['cell'] = 1
                part['lods'] = [0]
            if name == 'heat_exchanger_-1' or name == 'heat_exchanger_1':
                part['cell'] = 1
            if name in ('vent_louvre', 'fan_shroud'):
                part['cell'] = 9
            if part['kind'] == 'cylinder':
                part['sides'] = [min(part['sides'][0], 10), min(part['sides'][1], 6), part['sides'][2]]
            if name == 'roof_pipe':
                piping['parts'].append(part)
                continue
            if name in ('communications_spine', 'spine_roof', 'rear_command_bridge'):
                protected['parts'].append(part)
                continue
            if name.startswith('recessed_foundation_course'):
                part['lods'] = [lod for lod in part['lods'] if lod != 2]
            retained.append(part)
        group['parts'] = retained
    def box(name, lo, hi, cell, lods=(0, 1), bevel=0):
        return {'name': name, 'kind': 'box', 'min': lo, 'max': hi, 'cell': cell,
                'lods': list(lods), 'bevel': bevel}
    for side in (-1, 1):
        x0, x1 = sorted((side*.143, side*.162))
        protected['parts'].append(box('signal_spine_armour_' + str(side), [x0,.24,-.25], [x1,.39,.195], 0))
        x0, x1 = sorted((side*.47, side*.49))
        piping['parts'].append(box('service_control_' + str(side), [x0,-.20,.12], [x1,-.065,.22], 15, (0,)))
        piping['parts'].append({'name': 'protected_service_pipe_' + str(side), 'kind': 'cylinder',
            'start': [side*.474,-.245,-.20], 'end': [side*.474,-.245,.23],
            'radius': .008, 'cell': 7, 'lods': [0,1], 'sides': [8,4,4]})
    groups['communications']['parts'].append(box('signal_panel_armour_hood',
        [-.123,.532,.025], [.123,.547,.08], 0, (0,1,2)))
    kit['modules'].extend((protected, piping))
    kit['intactModules'].extend((protected['name'], piping['name']))
    kit['provenance']['source'] = 'Retained component kit with explicit Blender bootstrap refinements; saved Blender master owns subsequent edits'
    return kit


def write_emissive(path, size=1024):
    """Original numeric linear mask in the retained glass atlas cell; no baked light."""
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    pixels = bytearray()
    for y in range(size):
        pixels.append(0)
        for x in range(size):
            # Atlas cell four, inset beyond the neighbouring steel/concrete cells.
            glass = .018 <= x/size <= .232 and .268 <= y/size <= .482
            damage = .018 <= x/size <= .232 and .518 <= y/size <= .732
            pane = x % (size//16) > size//128 and y % (size//32) > size//256
            pixels.extend((28,100,130,255) if glass and pane else
                          (150,36,8,255) if damage and pane else (0,0,0,255))
    content = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size,size,8,6,0,0,0))
    content += chunk(b'IDAT', zlib.compress(bytes(pixels), 9)) + chunk(b'IEND', b'')
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_bytes(content)


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('--emissive-output', type=Path, required=True)
    write_emissive(parser.parse_args().emissive_output)
