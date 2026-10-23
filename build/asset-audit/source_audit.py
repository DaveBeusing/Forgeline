"""Read-only integrity census for production glTF/GLB, texture and material sources.

Structural failures are errors. Shading/geometry heuristics are warnings for review,
not permission to rewrite authored topology. Blender masters are inventoried only.
The production compiler and native renderer remain separate acceptance gates.
"""
from __future__ import annotations

import argparse
import base64
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import warnings
import zipfile

import numpy as np
from PIL import Image

DTYPES = {5120: '<i1', 5121: '<u1', 5122: '<i2', 5123: '<u2', 5125: '<u4', 5126: '<f4'}
WIDTHS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT2': 4, 'MAT3': 9, 'MAT4': 16}
ID_PATTERN = re.compile(r'[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\Z')


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def read_json(raw: bytes) -> dict:
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, f'duplicate JSON property: {key}')
            result[key] = value
        return result
    def invalid(value):
        raise ValueError(f'nonfinite JSON number: {value}')
    value = json.loads(raw.decode('utf-8-sig'), object_pairs_hook=pairs, parse_constant=invalid)
    require(isinstance(value, dict), 'JSON root must be an object')
    return value


def contained(root: Path, parent: Path, relative: str) -> Path:
    require(isinstance(relative, str) and bool(relative), 'missing source path')
    require(not Path(relative).is_absolute() and not re.match(r'^[A-Za-z][\w+.-]*:', relative), 'absolute/network source path')
    path = (parent / relative).resolve()
    require(path.is_relative_to(root.resolve()), 'source path escapes asset root')
    require(path.is_file(), f'missing source: {relative}')
    return path


def load_mesh(path: Path, root: Path) -> tuple[dict, list[bytes]]:
    raw = path.read_bytes()
    binary = None
    if path.suffix.lower() == '.glb':
        require(len(raw) >= 20, 'truncated GLB header')
        require(struct.unpack_from('<III', raw) == (0x46546c67, 2, len(raw)), 'invalid GLB magic/version/length')
        offset, chunks = 12, []
        while offset < len(raw):
            require(offset + 8 <= len(raw), 'truncated GLB chunk header')
            size, kind = struct.unpack_from('<II', raw, offset)
            require(size % 4 == 0 and offset + 8 + size <= len(raw), 'invalid GLB chunk length/alignment')
            chunks.append((kind, raw[offset + 8:offset + 8 + size]))
            offset += 8 + size
        require(chunks[0][0] == 0x4e4f534a, 'first GLB chunk must be JSON')
        require(sum(kind == 0x4e4f534a for kind, _ in chunks) == 1, 'duplicate GLB JSON chunk')
        bins = [data for kind, data in chunks if kind == 0x004e4942]
        require(len(bins) <= 1, 'duplicate GLB binary chunk')
        binary = bins[0] if bins else None
        document = read_json(chunks[0][1])
    else:
        document = read_json(raw)
    require(document.get('asset', {}).get('version') == '2.0', 'glTF 2.0 required')
    buffers = []
    for index, buffer in enumerate(document.get('buffers', [])):
        uri = buffer.get('uri')
        if uri is None:
            require(index == 0 and binary is not None, 'missing GLB buffer')
            content = binary
        elif uri.startswith('data:'):
            require(';base64,' in uri, 'unsupported buffer data URI')
            content = base64.b64decode(uri.split(',', 1)[1], validate=True)
        else:
            from urllib.parse import unquote
            content = contained(root, path.parent, unquote(uri)).read_bytes()
        length = buffer['byteLength']
        require(isinstance(length, int) and 0 <= length <= len(content), 'buffer length exceeds available data')
        buffers.append(content[:length])
    for view in document.get('bufferViews', []):
        data = buffers[view['buffer']]
        start, length = view.get('byteOffset', 0), view['byteLength']
        require(isinstance(start, int) and isinstance(length, int) and start >= 0 and length >= 0 and start + length <= len(data), 'buffer view exceeds buffer')
    return document, buffers


def accessor(document: dict, buffers: list[bytes], index: int) -> np.ndarray:
    require(isinstance(index, int) and 0 <= index < len(document.get('accessors', [])), 'invalid accessor index')
    acc = document['accessors'][index]
    require('sparse' not in acc, 'sparse accessor requires dedicated qualification (not inspected)')
    count = acc['count']
    require(isinstance(count, int) and 0 < count <= 32_000_000, 'invalid accessor count')
    dtype = np.dtype(DTYPES[acc['componentType']])
    width = WIDTHS[acc['type']]
    require(not (acc['type'] in ('MAT2', 'MAT3') and dtype.itemsize < 4), 'packed matrix accessor requires dedicated qualification (not inspected)')
    view = document['bufferViews'][acc['bufferView']]
    data = buffers[view['buffer']]
    size = dtype.itemsize * width
    start, offset = view.get('byteOffset', 0), acc.get('byteOffset', 0)
    stride = view.get('byteStride', size)
    require(offset >= 0 and stride >= size and stride % dtype.itemsize == 0 and (start + offset) % dtype.itemsize == 0, 'invalid accessor offset/stride/alignment')
    require(offset + (count - 1) * stride + size <= view['byteLength'], 'accessor exceeds buffer view')
    require(start + offset + (count - 1) * stride + size <= len(data), 'accessor exceeds buffer')
    values = np.ndarray((count, width), dtype=dtype, buffer=data, offset=start + offset, strides=(stride, dtype.itemsize))
    require(np.isfinite(values).all(), 'nonfinite accessor value')
    if acc.get('normalized', False):
        require(dtype.kind in ('i', 'u') and dtype.itemsize <= 2, 'invalid normalized accessor')
        values = values.astype(np.float64) / np.iinfo(dtype).max
        if dtype.kind == 'i':
            values = np.maximum(values, -1)
    return values


def mesh_stats(document: dict, buffers: list[bytes]) -> tuple[dict, list[tuple[str, str]]]:
    issues = []
    stats = {'primitives': 0, 'vertices': 0, 'triangles': 0, 'degenerateTriangles': 0, 'opposedNormalTriangles': 0,
             'nonunitNormals': 0, 'nonorthogonalTangents': 0, 'degenerateUvTriangles': 0}
    points = []
    for mi, mesh in enumerate(document.get('meshes', [])):
        for pi, primitive in enumerate(mesh.get('primitives', [])):
            label = f'mesh {mi} primitive {pi}'
            require(primitive.get('mode', 4) == 4, f'{label}: unsupported non-triangle topology')
            require(not primitive.get('extensions'), f'{label}: compressed/extended primitive requires dedicated inspection')
            attrs = {name: accessor(document, buffers, ai) for name, ai in primitive['attributes'].items()}
            require('POSITION' in attrs, f'{label}: missing POSITION')
            p = attrs['POSITION'].astype(np.float64)
            require(p.shape[1] == 3, f'{label}: POSITION must be VEC3')
            require(all(len(a) == len(p) for a in attrs.values()), f'{label}: mismatched attribute counts')
            if 'indices' in primitive:
                ai = primitive['indices']
                require(document['accessors'][ai]['componentType'] in (5121, 5123, 5125) and document['accessors'][ai]['type'] == 'SCALAR', f'{label}: invalid index accessor')
                idx = accessor(document, buffers, ai).reshape(-1).astype(np.int64)
            else:
                idx = np.arange(len(p))
            require(len(idx) > 0 and len(idx) % 3 == 0 and idx.min() >= 0 and idx.max() < len(p), f'{label}: invalid triangle indices')
            tris = idx.reshape(-1, 3)
            face = np.cross(p[tris[:, 1]] - p[tris[:, 0]], p[tris[:, 2]] - p[tris[:, 0]])
            area = np.linalg.norm(face, axis=1)
            degenerate = int(np.count_nonzero(area == 0))
            stats['degenerateTriangles'] += degenerate
            if degenerate:
                issues.append(('zero-area-triangles', f'{label}: {degenerate}/{len(tris)} exactly zero-area triangles'))
            if 'NORMAL' in attrs:
                normal = attrs['NORMAL'].astype(np.float64)
                require(normal.shape[1] == 3, f'{label}: NORMAL must be VEC3')
                lengths = np.linalg.norm(normal, axis=1)
                require(np.all(lengths > 1e-12), f'{label}: zero-length normals')
                nonunit = int(np.count_nonzero(np.abs(lengths - 1) > 0.002))
                stats['nonunitNormals'] += nonunit
                if nonunit:
                    issues.append(('nonunit-normals', f'{label}: {nonunit} non-unit normals'))
                avg = normal[tris].mean(axis=1)
                dot = np.einsum('ij,ij->i', face, avg)
                opposed = int(np.count_nonzero((area > 0) & (dot < -area * 0.01)))
                stats['opposedNormalTriangles'] += opposed
                if opposed:
                    issues.append(('normal-winding-disagreement', f'{label}: {opposed}/{len(tris)} face normals oppose winding; inspect intentional backfaces'))
            if 'TANGENT' in attrs:
                tangent = attrs['TANGENT'].astype(np.float64)
                require(tangent.shape[1] == 4 and np.all(np.abs(np.abs(tangent[:, 3]) - 1) <= 1e-5), f'{label}: invalid tangent handedness')
                lengths = np.linalg.norm(tangent[:, :3], axis=1)
                require(np.all(lengths > 1e-12), f'{label}: zero-length tangents')
                if 'NORMAL' in attrs:
                    dots = np.abs(np.einsum('ij,ij->i', tangent[:, :3], normal)) / (lengths * np.linalg.norm(normal, axis=1))
                    count = int(np.count_nonzero(dots > 0.002))
                    stats['nonorthogonalTangents'] += count
                    if count:
                        issues.append(('nonorthogonal-tangents', f'{label}: {count} tangents are not perpendicular to normals'))
            if 'TEXCOORD_0' in attrs:
                uv = attrs['TEXCOORD_0'].astype(np.float64)
                require(uv.shape[1] == 2, f'{label}: TEXCOORD_0 must be VEC2')
                a, b = uv[tris[:, 1]] - uv[tris[:, 0]], uv[tris[:, 2]] - uv[tris[:, 0]]
                count = int(np.count_nonzero((area > 0) & (np.abs(a[:, 0] * b[:, 1] - a[:, 1] * b[:, 0]) < 1e-12)))
                stats['degenerateUvTriangles'] += count
                if count:
                    issues.append(('degenerate-uv-triangles', f'{label}: {count} nondegenerate faces have collapsed UVs'))
            for name in ('WEIGHTS_0', 'WEIGHTS_1'):
                if name in attrs:
                    require(np.all(attrs[name] >= 0), f'{label}: negative skin weights')
            if 'WEIGHTS_0' in attrs:
                weights = attrs['WEIGHTS_0'].sum(axis=1)
                if 'WEIGHTS_1' in attrs:
                    weights += attrs['WEIGHTS_1'].sum(axis=1)
                require(np.all(np.abs(weights - 1) < 0.01), f'{label}: unnormalized skin weights')
            pa = document['accessors'][primitive['attributes']['POSITION']]
            for key, actual in [('min', p.min(axis=0)), ('max', p.max(axis=0))]:
                if key in pa and not np.allclose(pa[key], actual, atol=1e-5, rtol=1e-6):
                    issues.append(('position-bounds-mismatch', f'{label}: declared POSITION {key} differs from decoded vertices'))
            stats['primitives'] += 1
            stats['vertices'] += len(p)
            stats['triangles'] += len(tris)
            points.extend([p.min(axis=0), p.max(axis=0)])
    require(stats['primitives'] > 0, 'no mesh primitives')
    stats['boundsMin'] = np.min(points, axis=0).tolist()
    stats['boundsMax'] = np.max(points, axis=0).tolist()
    stats['skins'] = len(document.get('skins', []))
    stats['animations'] = len(document.get('animations', []))
    for i in range(len(document.get('accessors', []))):
        accessor(document, buffers, i)
    return stats, issues


def texture_stats(path: Path, usages: set[str]) -> tuple[dict, list[tuple[str, str]]]:
    with warnings.catch_warnings():
        warnings.simplefilter('error', Image.DecompressionBombWarning)
        with Image.open(path) as image:
            image.verify()
        with Image.open(path) as image:
            image.load()
            stats = {'width': image.width, 'height': image.height, 'mode': image.mode, 'format': image.format}
            require(image.width <= 16384 and image.height <= 16384, 'texture exceeds runtime dimension limit')
            issues = []
            if 'normal' in usages:
                invalid, tested = 0, 0
                for y in range(0, image.height, 128):
                    rgba = np.asarray(image.crop((0, y, image.width, min(y + 128, image.height))).convert('RGBA'))
                    xyz = rgba[:, :, :3].astype(np.float32) / 127.5 - 1
                    visible = rgba[:, :, 3] > 0
                    length = np.linalg.norm(xyz, axis=2)
                    tested += int(visible.sum())
                    invalid += int(np.count_nonzero(visible & ((length < 0.75) | (length > 1.25))))
                stats.update(normalPixels=tested, nonunitNormalPixels=invalid)
                if invalid:
                    issues.append(('normal-texture-length', f'{invalid}/{tested} visible normals outside [0.75,1.25]; inspect masks/encoding'))
            return stats, issues


def audit(root: Path) -> dict:
    root = root.resolve()
    require(root.is_dir(), 'asset source root does not exist')
    files, findings = [], []
    definitions, sources, usages = {}, defaultdict(list), defaultdict(set)
    def finding(severity, code, path, message):
        findings.append({'severity': severity, 'code': code, 'path': str(path.relative_to(root)), 'message': message})
    for path in sorted(root.rglob('*.asset.json')):
        try:
            d = read_json(path.read_bytes())
            aid = d['id']
            require(isinstance(aid, str) and ID_PATTERN.fullmatch(aid), 'invalid stable asset ID')
            require(aid not in definitions, f'duplicate asset ID: {aid}')
            source = contained(root, path.parent, d['source'])
            definitions[aid] = (path, d, source)
            sources[source].append(aid)
            if d['type'].lower() == 'texture':
                usage = d.get('textureUsage', 'baseColor').lower()
                usages[source].add(usage)
                required = 'linear' if usage in ('normal', 'orm', 'genericdata', 'terraincontrol') else 'srgb'
                require(d.get('textureColorSpace', 'srgb').lower() == required, f'{usage} texture must use {required} color space')
            require(d.get('scale', 1) == 1, 'non-unit import scale')
        except (ValueError, KeyError, TypeError, OSError) as error:
            finding('error', 'asset-definition', path, str(error))
    for aid, (path, d, source) in definitions.items():
        try:
            refs = [(v, None) for v in d.get('dependencies', [])]
            for key, kind in [('materialReferences', 'material'), ('textureReferences', 'texture'), ('animationReferences', 'animation')]:
                refs.extend((v, kind) for v in d.get(key, []))
            refs.extend((v['assetId'], 'mesh') for v in d.get('lods', []))
            for key, kind in [('collisionReference', 'mesh'), ('skinReference', 'skin')]:
                if d.get(key):
                    refs.append((d[key], kind))
            if d.get('collisionRequired'):
                require(bool(d.get('collisionReference')), 'required collision reference missing')
            if d['type'].lower() == 'material':
                material = read_json(source.read_bytes())
                for key, usage in [('baseColorTexture', 'basecolor'), ('normalTexture', 'normal'), ('ormTexture', 'orm'), ('emissiveTexture', 'emissive')]:
                    if material.get(key):
                        target = material[key]
                        refs.append((target, 'texture'))
                        require(target in definitions, f'missing {key}: {target}')
                        actual = definitions[target][1].get('textureUsage', 'baseColor').lower()
                        require(actual == usage or (usage == 'basecolor' and actual == 'color'), f'{key} resolves to incompatible usage {actual}')
            for target, kind in refs:
                require(target in definitions, f'missing reference: {target}')
                if kind:
                    require(definitions[target][1]['type'].lower() == kind, f'{target} is not {kind}')
        except (ValueError, KeyError, TypeError, OSError) as error:
            finding('error', 'asset-reference', path, str(error))
    for path in sorted(root.rglob('*')):
        if not path.is_file() or path.suffix.lower() not in ('.gltf', '.glb', '.png', '.tga', '.blend'):
            continue
        record = {'path': str(path.relative_to(root)), 'bytes': path.stat().st_size,
                  'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'assetIds': sources.get(path, []),
                  'kind': 'mesh' if path.suffix.lower() in ('.gltf', '.glb') else 'master' if path.suffix.lower() == '.blend' else 'texture'}
        try:
            if record['kind'] == 'mesh':
                stats, issues = mesh_stats(*load_mesh(path, root))
            elif record['kind'] == 'texture':
                stats, issues = texture_stats(path, usages[path])
            else:
                record['status'] = 'inventoried-not-opened'
                files.append(record)
                continue
            record.update(status='checked', stats=stats)
            for code, message in issues:
                finding('warning', code, path, message)
        except (ValueError, KeyError, IndexError, TypeError, OSError, struct.error, Image.DecompressionBombError, Image.DecompressionBombWarning) as error:
            record['status'] = 'failed'
            finding('error', 'source-data', path, str(error))
        files.append(record)
    counts = Counter(f['kind'] for f in files)
    return {'schema': 1, 'scope': 'assets/source exported geometry, textures and definitions; no native visual acceptance',
            'summary': {'definitions': len(definitions), **dict(counts), 'errors': sum(f['severity'] == 'error' for f in findings),
                        'warnings': sum(f['severity'] == 'warning' for f in findings)}, 'files': files, 'findings': findings}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path('assets/source'))
    parser.add_argument('--report', type=Path, default=Path('artifacts/asset-audit/report.json'))
    parser.add_argument('--review-bundle', type=Path, help='Optional mesh/metadata archive for reproducible inspection; no fonts/textures')
    args = parser.parse_args()
    result = audit(args.root)
    try:
        result['sourceCommit'] = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
    except (OSError, subprocess.CalledProcessError):
        result['sourceCommit'] = None
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    if args.review_bundle:
        args.review_bundle.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(args.review_bundle, 'w', zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(args.root.rglob('*')):
                if path.is_file() and path.suffix.lower() in ('.gltf', '.glb', '.bin', '.json') and 'fonts' not in path.parts:
                    archive.write(path, 'source/' + path.relative_to(args.root).as_posix())
    print(json.dumps(result['summary'], sort_keys=True))
    for issue in result['findings'][:100]:
        print(f"{issue['severity']} {issue['code']} {issue['path']}: {issue['message']}")
    print(f"Complete report: {args.report}")
    return 1 if result['summary']['errors'] else 0


if __name__ == '__main__':
    raise SystemExit(main())
