"""Read actual glTF corner attributes to guard infantry atlas orientation."""
import base64
import json
from pathlib import Path
import struct


def document(path):
    raw = Path(path).read_bytes()
    if raw[:4] == b'glTF':
        size = struct.unpack_from('<I', raw, 12)[0]
        return json.loads(raw[20:20 + size]), raw[28 + size:]
    root = json.loads(raw.decode('utf-8-sig'))
    uri = root['buffers'][0]['uri']
    return root, base64.b64decode(uri.split(',', 1)[1]) if uri.startswith('data:') else (Path(path).parent / uri).read_bytes()


def accessor(root, binary, index):
    entry = root['accessors'][index]; view = root['bufferViews'][entry['bufferView']]
    fmt = {5121:'B', 5123:'H', 5125:'I', 5126:'f'}[entry['componentType']]
    width = {'SCALAR':1, 'VEC2':2, 'VEC3':3, 'VEC4':4, 'MAT4':16}[entry['type']]
    unpack = struct.Struct('<' + fmt * width)
    offset = view.get('byteOffset', 0) + entry.get('byteOffset', 0)
    stride = view.get('byteStride', unpack.size)
    return [unpack.unpack_from(binary, offset + i * stride) for i in range(entry['count'])]


def corners(path):
    root, binary = document(path)
    result = []
    for primitive in root['meshes'][0]['primitives']:
        attrs = {name:accessor(root, binary, index) for name, index in primitive['attributes'].items()}
        indices = [v[0] for v in accessor(root, binary, primitive['indices'])]
        result.extend({name:values[i] for name, values in attrs.items()} for i in indices)
    return result


def validate_surface(static_path, skinned_path):
    expected, actual = corners(static_path), corners(skinned_path)
    if len(expected) != len(actual):
        raise ValueError('Infantry triangle count changed')
    # Imported Blender custom normals already have quantization relative to the
    # static recipe. Preservation compares them exactly against the original GLB;
    # this cross-format check owns corner positions and atlas coordinates only.
    worst = {'POSITION':0.0, 'TEXCOORD_0':0.0}
    for before, after in zip(expected, actual):
        for name in worst:
            worst[name] = max(worst[name], *(abs(a-b) for a,b in zip(before[name], after[name])))
    for name, error in worst.items():
        if error > (1e-6 if name == 'TEXCOORD_0' else 1e-5):
            raise ValueError(f'Infantry {name} differs from static LOD0: {error}')
    return dict(corners=len(actual), maximumErrors=worst)


def validate_preservation(before_path, after_path):
    before, after = corners(before_path), corners(after_path)
    if len(before) != len(after): raise ValueError('Changed triangle count')
    for a, b in zip(before, after):
        for name in ('POSITION', 'NORMAL', 'JOINTS_0', 'WEIGHTS_0'):
            if a[name] != b[name]: raise ValueError('Changed ' + name)
    old, old_binary = document(before_path); new, new_binary = document(after_path)
    if old['skins'] != new['skins'] or old['nodes'] != new['nodes']:
        raise ValueError('Changed rig hierarchy or rest transforms')
    for a, b in zip(old['skins'], new['skins']):
        if accessor(old, old_binary, a['inverseBindMatrices']) != accessor(new, new_binary, b['inverseBindMatrices']):
            raise ValueError('Changed inverse binds')
    if [a['name'] for a in old['animations']] != [a['name'] for a in new['animations']]:
        raise ValueError('Changed actions')
    for a, b in zip(old['animations'], new['animations']):
        if a['channels'] != b['channels'] or len(a['samplers']) != len(b['samplers']):
            raise ValueError('Changed action channels')
        for x, y in zip(a['samplers'], b['samplers']):
            if x.get('interpolation') != y.get('interpolation'):
                raise ValueError('Changed interpolation')
            for name in ('input', 'output'):
                if accessor(old, old_binary, x[name]) != accessor(new, new_binary, y[name]):
                    raise ValueError('Changed action keys')
    if any(new.get(name) for name in ('images', 'textures')):
        raise ValueError('Native preview textures leaked into runtime skin source')
    return dict(corners=len(after), joints=len(new['skins'][0]['joints']), actions=len(new['animations']))


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate', type=Path, required=True)
    parser.add_argument('--baseline', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    source = Path(__file__).resolve().parents[2] / 'assets/source/units/directorate'
    records = {}
    for family in ('rifle_squad', 'combat_engineer'):
        entry = validate_surface(source / f'sources/infantry/{family}_lod0.gltf', args.candidate / f'{family}.glb')
        if args.baseline:
            entry['preserved'] = validate_preservation(args.baseline / f'{family}.glb', args.candidate / f'{family}.glb')
            try:
                validate_surface(source / f'sources/infantry/{family}_lod0.gltf', args.baseline / f'{family}.glb')
            except ValueError as error:
                entry['originalFailure'] = str(error)
            else:
                raise ValueError('Original UV defect was not reproduced')
        records[family] = entry
    report = json.dumps(dict(success=True, families=records), indent=2) + '\n'
    if args.report: args.report.write_text(report, encoding='utf-8')
    print(report)
