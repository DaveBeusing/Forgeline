"""Author bounded volumetric foliage with matching rooted high/reduced silhouettes.

Uses each committed source's existing atlas rectangle; no new materials or textures.
Run from any directory. Requires only Python's standard library.
"""
import base64
import json
import math
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / 'assets/source/world/meshes/sources'


def author(family, reduced):
    path = SOURCES / f'mesh_world_vegetation_{family}{"_lod1" if reduced else ""}.gltf'
    old = json.loads(path.read_text())
    primitive = old['meshes'][0]['primitives'][0]
    accessor = old['accessors'][primitive['attributes']['TEXCOORD_0']]
    view = old['bufferViews'][accessor['bufferView']]
    raw = base64.b64decode(old['buffers'][0]['uri'].split(',')[1])
    uv = list(struct.iter_unpack('<2f', raw[view.get('byteOffset', 0):view.get('byteOffset', 0) + accessor['count'] * 8]))
    lo = [min(p[i] for p in uv) for i in range(2)]
    hi = [max(p[i] for p in uv) for i in range(2)]
    positions, normals, coords, indices = [], [], [], []

    def triangle(a, b, c):
        u = [b[i] - a[i] for i in range(3)]
        v = [c[i] - a[i] for i in range(3)]
        n = [u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]]
        length = math.sqrt(sum(x*x for x in n))
        assert length > 1e-8
        n = [x/length for x in n]
        for p, tex in zip((a, b, c), ((0, 1), (1, 1), (.5, 0))):
            indices.append(len(positions))
            positions.append(p)
            normals.append(n)
            coords.append([lo[i] + tex[i] * (hi[i]-lo[i]) for i in range(2)])

    def crown(bottom, top, radius, sides, phase=0):
        tip = (0, top, 0)
        for i in range(sides):
            a = phase + i * math.tau / sides
            b = phase + (i + 1) * math.tau / sides
            p = (radius*math.cos(a), bottom, radius*math.sin(a))
            q = (radius*math.cos(b), bottom, radius*math.sin(b))
            triangle(q, p, tip)
            triangle(p, q, (0, bottom, 0))

    if family == 'conifer':
        sides = 6 if reduced else 10
        crown(0, .65, .055, sides)
        for bottom, top, radius in ((.16, .65, .5), (.40, .84, .38), (.65, 1, .23)):
            crown(bottom, top, radius, sides)
    elif family == 'scrub':
        # Two overlapping crowns retain the same envelope in both levels.
        crown(0, .72, .5, 6 if reduced else 10)
        crown(.2, 1, .34, 6 if reduced else 10, math.pi/6)
    else:
        for i in range(3 if reduced else 6):
            angle = i * math.pi / (3 if reduced else 6)
            x, z = .5*math.cos(angle), .5*math.sin(angle)
            a, b, c = (-x, 0, -z), (x, 0, z), (.12*x, 1, .12*z)
            triangle(a, b, c)
            triangle(c, b, a)

    arrays = [(positions, 'VEC3', 5126, 'f'), (normals, 'VEC3', 5126, 'f'),
              (coords, 'VEC2', 5126, 'f'), ([(i,) for i in indices], 'SCALAR', 5125, 'I')]
    data, views, accessors = b'', [], []
    for values, kind, component, fmt in arrays:
        flat = [x for row in values for x in row]
        payload = struct.pack('<' + fmt * len(flat), *flat)
        views.append(dict(buffer=0, byteOffset=len(data), byteLength=len(payload)))
        accessors.append(dict(bufferView=len(views)-1, componentType=component, count=len(values), type=kind))
        data += payload
    output = dict(asset=dict(version='2.0', generator='ForgeLine world vegetation authoring'),
        buffers=[dict(byteLength=len(data), uri='data:application/octet-stream;base64,' + base64.b64encode(data).decode())],
        bufferViews=views, accessors=accessors,
        meshes=[dict(primitives=[dict(attributes=dict(POSITION=0, NORMAL=1, TEXCOORD_0=2), indices=3, mode=4)])])
    path.write_text(json.dumps(output, indent=2) + '\n')
    print(path.name, len(indices)//3, 'triangles')


if __name__ == '__main__':
    for family in ('conifer', 'scrub', 'grass_clump'):
        for reduced in (False, True):
            author(family, reduced)
