"""Strict authoring adapter for the existing static compiler subset; standard library only."""
import base64
import copy
import hashlib
import json
import math
import re
import shutil
import struct
from pathlib import Path
from urllib.parse import unquote

POSITION_EPS = 1e-5
DIRECTION_EPS = 1e-5
UV_EPS = 1e-6
SOCKET_EPS = 1e-6
WORLD_EPS = 2e-5


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True, allow_nan=False) + "\n", encoding="utf-8")


def contained(root, path):
    root, path = Path(root).resolve(), Path(path).resolve()
    require(path.is_relative_to(root), f"Path escapes source/output root: {path}")
    return path


def game_to_blender(v):
    return (v[0], -v[2], v[1])


def blender_to_game(v):
    return (v[0], v[2], -v[1])


def socket_world(v, family):
    scaled = [v[i] * family['instanceScale'][i] for i in range(3)]
    yaw = math.radians(family['instanceYawDegrees'])
    x, y, z = scaled
    rotated = [x * math.cos(yaw) + z * math.sin(yaw), y, -x * math.sin(yaw) + z * math.cos(yaw)]
    return [rotated[i] + family['instanceTranslation'][i] for i in range(3)]


def load_gltf(path, root):
    path = contained(root, path)
    raw = path.read_bytes()
    binary = None
    if path.suffix.lower() == '.glb':
        require(len(raw) >= 20 and struct.unpack_from('<III', raw) == (0x46546c67, 2, len(raw)), 'Invalid GLB header')
        size, kind = struct.unpack_from('<II', raw, 12)
        require(kind == 0x4e4f534a and 20 + size <= len(raw), 'Invalid GLB JSON chunk')
        document = json.loads(raw[20:20 + size])
        end = 20 + size
        if end < len(raw):
            require(end + 8 <= len(raw), 'Invalid GLB binary chunk')
            size, kind = struct.unpack_from('<II', raw, end)
            require(kind == 0x004e4942 and end + 8 + size == len(raw), 'Only one GLB binary chunk is supported')
            binary = raw[end + 8:]
    else:
        require(path.suffix.lower() == '.gltf', 'Only glTF/GLB sources are supported')
        document = json.loads(raw)
    require(document.get('asset', {}).get('version') == '2.0', 'glTF 2.0 is required')
    for key in ('animations', 'skins', 'cameras', 'extensionsRequired', 'extensionsUsed', 'extensions', 'images', 'textures', 'samplers'):
        require(not document.get(key), f'Unsupported glTF feature: {key}; do not silently drop it')
    buffers, files = [], [path]
    for buffer in document.get('buffers', []):
        uri = buffer.get('uri')
        if uri is None:
            require(binary is not None and not buffers, 'Missing GLB binary buffer')
            content = binary
        elif uri.startswith('data:'):
            require(';base64,' in uri, 'Only base64 buffer data URIs are supported')
            content = base64.b64decode(uri.split(',', 1)[1], validate=True)
        else:
            require(not re.match(r'^[A-Za-z][A-Za-z0-9+.-]*:', uri), 'External network/absolute buffer URI is unsupported')
            file = contained(root, path.parent / unquote(uri))
            content = file.read_bytes(); files.append(file)
        require(len(content) >= buffer['byteLength'], 'Truncated glTF buffer')
        buffers.append(content)
    require(document.get('meshes'), 'Source has no meshes')
    references = []
    for node in document.get('nodes', []):
        for key, identity in [('translation', [0,0,0]), ('rotation', [0,0,0,1]), ('scale', [1,1,1]), ('matrix', [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1])]:
            require(key not in node or node[key] == identity, 'Bake glTF node transforms; compiler reads mesh data, not node transforms')
        require('camera' not in node and not node.get('extensions') and 'skin' not in node and not node.get('weights'), 'Unsupported node skin/extension/morph')
        if 'mesh' in node: references.append(node['mesh'])
    require(not document.get('nodes') or sorted(references) == list(range(len(document['meshes']))), 'Each declared source mesh must be instantiated exactly once')
    for material in document.get('materials', []):
        require(set(material) <= {'name','pbrMetallicRoughness','doubleSided','alphaMode','alphaCutoff','emissiveFactor','extras'}, 'Unsupported embedded material feature')
        pbr = material.get('pbrMetallicRoughness', {})
        require(set(pbr) <= {'baseColorFactor','metallicFactor','roughnessFactor'}, 'PBR texture bindings belong to asset material definitions')
    return document, buffers, files


def read_accessor(document, buffers, index, semantic):
    acc = document['accessors'][index]
    require('sparse' not in acc and not acc.get('normalized'), 'Sparse/normalized accessors are unsupported')
    count = acc['count']; require(isinstance(count, int) and 0 < count <= 2_000_000, 'Invalid/bounded accessor count')
    expected = {'POSITION':'VEC3','NORMAL':'VEC3','TEXCOORD_0':'VEC2','TANGENT':'VEC4','indices':'SCALAR'}[semantic]
    require(acc['type'] == expected, f'Invalid {semantic} accessor type')
    component = acc['componentType']
    require(component in (5121,5123,5125) if semantic == 'indices' else component == 5126, f'Unsupported {semantic} component type')
    scalar = {5121:'B',5123:'H',5125:'I',5126:'f'}[component]
    width = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[expected]
    fmt = '<' + scalar * width; size = struct.calcsize(fmt)
    view = document['bufferViews'][acc['bufferView']]
    require(not view.get('extensions'), 'Compressed buffer views are unsupported')
    content = buffers[view['buffer']]
    offset = acc.get('byteOffset', 0); start = view.get('byteOffset', 0) + offset
    stride = view.get('byteStride', size)
    require(offset >= 0 and stride >= size and (count - 1)*stride + offset + size <= view['byteLength'], 'Accessor exceeds buffer view')
    require(start >= 0 and start + (count - 1)*stride + size <= len(content), 'Accessor exceeds buffer')
    values = [tuple(struct.unpack_from(fmt, content, start + i*stride)) for i in range(count)]
    require(all(math.isfinite(v) for row in values for v in row), f'Nonfinite {semantic}')
    return values


def read_geometry(path, root, definition):
    document, buffers, files = load_gltf(path, root)
    primitives = []
    refs = definition.get('materialReferences', [])
    for mi, mesh in enumerate(document['meshes']):
        require(not mesh.get('weights'), 'Morph weights are unsupported')
        for pi, primitive in enumerate(mesh['primitives']):
            require(primitive.get('mode', 4) == 4, 'Only triangle primitives are supported')
            require(not primitive.get('targets') and not primitive.get('extensions'), 'Morph/compressed primitive is unsupported')
            attrs = primitive['attributes']
            require('POSITION' in attrs and set(attrs) <= {'POSITION','NORMAL','TEXCOORD_0','TANGENT'}, 'Unsupported vertex attributes')
            arrays = {key:read_accessor(document, buffers, index, key) for key,index in attrs.items()}
            n = len(arrays['POSITION'])
            require(all(len(x) == n for x in arrays.values()), 'Mismatched vertex attribute counts')
            indices = [x[0] for x in read_accessor(document,buffers,primitive['indices'],'indices')] if 'indices' in primitive else list(range(n))
            require(len(indices) % 3 == 0 and all(0 <= i < n for i in indices), 'Invalid triangle indices')
            require(set(indices) == set(range(n)), 'Unused vertices require deliberate cleanup before import')
            slot = primitive.get('material', 0 if len(refs) == 1 else -1)
            require((not refs and slot == -1) or 0 <= slot < len(refs), 'Material slot has no stable material reference')
            require(len(refs) <= 1 or 'material' in primitive, 'Multiple materials require explicit primitive slots')
            primitives.append({'mesh':mi,'primitive':pi,'name':mesh.get('name',f'mesh_{mi}'),'materialSlot':slot,'attributes':arrays,'indices':indices})
    return {'document':document,'primitives':primitives,'files':files}


def geometry_stats(geometry):
    points = [v for p in geometry['primitives'] for v in p['attributes']['POSITION']]
    return {'vertices':len(points),'triangles':sum(len(p['indices'])//3 for p in geometry['primitives']),
            'primitives':len(geometry['primitives']),'boundsMin':[min(p[i] for p in points) for i in range(3)],'boundsMax':[max(p[i] for p in points) for i in range(3)]}


def compare_geometry(baseline, exported):
    require(len(baseline['primitives']) == len(exported['primitives']), 'Primitive count changed')
    deviations = {'POSITION':0.0,'NORMAL':0.0,'TEXCOORD_0':0.0,'TANGENT':0.0}
    for before, after in zip(baseline['primitives'],exported['primitives']):
        for key in ('mesh','primitive','name','materialSlot','indices'):
            require(before[key] == after[key], f'Geometry identity/topology changed: {key}')
        require(set(before['attributes']) == set(after['attributes']), 'Vertex channels changed')
        for key, old in before['attributes'].items():
            new = after['attributes'][key]
            require(len(old) == len(new), 'Vertex count changed')
            error = max(abs(a-b) for x,y in zip(old,new) for a,b in zip(x,y))
            eps = POSITION_EPS if key == 'POSITION' else UV_EPS if key == 'TEXCOORD_0' else DIRECTION_EPS
            require(error <= eps, f'{key} deviation {error} exceeds {eps}; check axes/scale/geometry')
            if key == 'TANGENT': require(all(a[3] == b[3] for a,b in zip(old,new)), 'Tangent handedness changed')
            deviations[key] = max(deviations[key],error)
    return deviations


def write_glb(path, geometry):
    document = copy.deepcopy(geometry['document'])
    for key in ('buffers','bufferViews','accessors'): document[key] = []
    binary = bytearray()
    def accessor(values, semantic):
        while len(binary)%4: binary.append(0)
        offset = len(binary)
        width = len(values[0]); scalar = 'I' if semantic == 'indices' else 'f'
        for row in values: binary.extend(struct.pack('<'+scalar*width,*row))
        view = len(document['bufferViews'])
        document['bufferViews'].append({'buffer':0,'byteOffset':offset,'byteLength':len(binary)-offset})
        acc = {'bufferView':view,'componentType':5125 if scalar == 'I' else 5126,'count':len(values),'type':{1:'SCALAR',2:'VEC2',3:'VEC3',4:'VEC4'}[width]}
        if semantic == 'POSITION':
            acc['min'] = [min(x[i] for x in values) for i in range(3)];acc['max'] = [max(x[i] for x in values) for i in range(3)]
        document['accessors'].append(acc);return len(document['accessors'])-1
    for primitive in geometry['primitives']:
        target = document['meshes'][primitive['mesh']]['primitives'][primitive['primitive']]
        target['attributes'] = {k:accessor(v,k) for k,v in primitive['attributes'].items()}
        target['indices'] = accessor([(i,) for i in primitive['indices']],'indices')
    document['buffers'] = [{'byteLength':len(binary)}]
    encoded = json.dumps(document,separators=(',',':'),allow_nan=False).encode()
    encoded += b' ' * (-len(encoded)%4);binary.extend(b'\x00' * (-len(binary)%4))
    payload = struct.pack('<III',0x46546c67,2,28+len(encoded)+len(binary))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(binary),0x004e4942)+binary
    Path(path).parent.mkdir(parents=True,exist_ok=True);Path(path).write_bytes(payload)


class Catalog:
    def __init__(self, root):
        self.root = Path(root).resolve(); self.definitions = {}
        for path in sorted(self.root.rglob('*.asset.json')):
            definition = json.loads(path.read_text(encoding='utf-8-sig'))
            aid = definition['id']
            require(aid not in self.definitions, f'Duplicate asset ID {aid}')
            self.definitions[aid] = (contained(self.root,path),definition)

    def source(self, aid):
        require(aid in self.definitions, f'Missing asset/state/reference {aid}')
        path, definition = self.definitions[aid]
        return contained(self.root,path.parent/definition['source'])

    def closure(self, ids):
        result = set(); active = set()
        def visit(aid):
            require(aid not in active,f'Cyclic asset/LOD references: {aid}')
            if aid in result:return
            active.add(aid)
            require(aid in self.definitions,f'Missing asset/state/reference {aid}')
            result.add(aid);_,d = self.definitions[aid]
            refs = list(d.get('dependencies',[]))+list(d.get('materialReferences',[]))+list(d.get('textureReferences',[]))
            require(not d.get('animationReferences'), 'Animation asset references are unsupported')
            refs += [x['assetId'] for x in d.get('lods',[])]
            if d.get('collisionReference'):refs.append(d['collisionReference'])
            if d['type'] == 'material':
                material = json.loads(self.source(aid).read_text(encoding='utf-8-sig'))
                refs += [material[k] for k in ('baseColorTexture','normalTexture','ormTexture','emissiveTexture') if material.get(k)]
            for ref in refs:visit(ref)
            active.remove(aid)
        for aid in ids:visit(aid)
        return sorted(result)

    def files(self, ids):
        files = set()
        for aid in ids:
            path,d = self.definitions[aid];files.update((path,self.source(aid)))
            if d['type'] == 'mesh':files.update(load_gltf(self.source(aid),self.root)[2])
        return sorted(files)

    def signature(self, ids):
        records = {str(p.relative_to(self.root)).replace('\\','/'):sha(p) for p in self.files(ids)}
        return hashlib.sha256(json.dumps(records,sort_keys=True).encode()).hexdigest(),records

    def copy_to(self, ids, destination):
        destination = Path(destination)
        for path in self.files(ids):
            target = contained(destination,destination/path.relative_to(self.root));target.parent.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(path,target)

    def validate(self, ids):
        for aid in ids:
            _,d = self.definitions[aid]
            if d['type'] != 'mesh':continue
            require(d.get('scale',1.0) == 1.0, f'Unit scale must be 1.0: {aid}')
            refs = d.get('materialReferences',[])
            require(all(self.definitions[x][1]['type']=='material' for x in refs), 'Material reference type mismatch')
            lods = d.get('lods',[])
            require([x['level'] for x in lods] == list(range(1,len(lods)+1)) and len(lods)<=2, 'Invalid LOD chain')
            distances = [x['maxDistance'] for x in lods]
            require(all(math.isfinite(v) and v>0 for v in distances) and distances == sorted(set(distances)), 'Invalid LOD distances')
            for lod in lods:
                target = self.definitions[lod['assetId']][1]
                require(target['type']=='mesh' and target.get('materialReferences',[])==refs, 'LOD material identity mismatch')
            collision = d.get('collisionReference')
            require(not d.get('collisionRequired') or collision, 'Missing collision reference')
            if collision:require(self.definitions[collision][1]['type']=='mesh','Collision reference must be a mesh')
            names = []
            for socket in d.get('sockets',[]):
                require(set(socket)=={'name','x','y','z'} and socket['name'] not in names and socket['name'], 'Invalid/duplicate socket')
                require(all(math.isfinite(socket[x]) for x in 'xyz'), 'Nonfinite socket')
                names.append(socket['name'])
            g = read_geometry(self.source(aid),self.root,d)
            total = sum(len(p['indices']) for p in g['primitives']);cursor = 0
            for i,part in enumerate(d.get('mechanicalParts',[])):
                require(part['firstIndex']==cursor and part['indexCount']>0 and part['indexCount']%3==0,'Invalid mechanical triangle partition')
                require(-1<=part.get('parent',-1)<i,'Invalid mechanical parent')
                require(not part.get('socket') or part['socket'] in names,'Missing mechanical socket')
                cursor += part['indexCount']
            require(not d.get('mechanicalParts') or cursor==total, 'Mechanical ranges do not cover geometry')


def fixture_selection(catalog, path, roots):
    fixture = json.loads(Path(path).read_text(encoding='utf-8-sig'))
    require(fixture.get('schemaVersion')==1,'Unsupported fixture contract')
    families = fixture['families']
    if not roots: roots = [x['root'] for x in families]
    selected = []
    for root in roots:
        family = next((x for x in families if x['root']==root),None)
        require(family is not None,f'Root has no validated family/state contract: {root}')
        require(root in family['assets'],'Fixture root is missing from its family')
        selected.extend(family['assets'])
    ids = catalog.closure(selected);catalog.validate(ids)
    return ids,[x for x in families if x['root'] in roots]
