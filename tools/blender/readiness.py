"""Source-derived visual inventory. Presence is never art or renderer acceptance."""
import argparse
import hashlib
import json
import struct
import subprocess
from collections import Counter
from pathlib import Path

from asset_contract import Catalog, contained, geometry_stats, read_geometry, sha, write_json

DOMAINS = ('buildings', 'units', 'infrastructure', 'world', 'materials')
TEXTURES = ('baseColorTexture', 'normalTexture', 'ormTexture', 'emissiveTexture')


def references(definition, material=None):
    result = []
    for key in ('dependencies', 'materialReferences', 'textureReferences', 'animationReferences'):
        result.extend(definition.get(key, []))
    result.extend(lod['assetId'] for lod in definition.get('lods', []))
    for key in ('collisionReference', 'skinReference', 'animationReference'):
        if definition.get(key):
            result.append(definition[key])
    if material:
        result.extend(material[key] for key in TEXTURES if material.get(key))
    return sorted(set(result))


def glb_document(path):
    raw = path.read_bytes()
    if len(raw) < 20 or struct.unpack_from('<III', raw) != (0x46546c67, 2, len(raw)):
        raise ValueError('Invalid GLB header')
    size, kind = struct.unpack_from('<II', raw, 12)
    if kind != 0x4e4f534a or 20 + size > len(raw):
        raise ValueError('Invalid GLB JSON chunk')
    return json.loads(raw[20:20 + size])


def owner(path):
    domain = path.split('/')[2]
    if domain == 'materials':
        return 'shared-materials'
    return domain


def saved_master(source, root):
    """Only known source bindings; a nearby unrelated .blend proves no coverage."""
    relative = source.relative_to(root).as_posix()
    if relative.startswith('units/directorate/sources/main_battle_tank/blender/'):
        candidate = root / 'units/directorate/authoring/main_battle_tank.blend'
    elif relative in ('units/directorate/authoring/infantry/rifle_squad.glb',
                      'units/directorate/authoring/infantry/combat_engineer.glb'):
        candidate = source.with_suffix('.blend')
    else:
        return None
    return candidate if candidate.is_file() else None


def runtime_record(record, runtime):
    if record is None:
        return {'status': 'not-observed'}
    payload = contained(runtime, runtime / record['runtimePath'])
    if not payload.is_file():
        return {'status': 'missing-container'}
    return {'status': 'container-present-not-qualified', 'path': record['runtimePath'],
            'containerSha256': sha(payload), 'bytes': payload.stat().st_size}


def inventory(repository, runtime=None):
    repository = Path(repository).resolve()
    root = repository / 'assets/source'
    catalog = Catalog(root)
    records = {}
    if runtime:
        runtime = Path(runtime).resolve()
        manifest = json.loads((runtime / 'manifest.json').read_text(encoding='utf-8-sig'))
        for record in manifest['assets']:
            if record['id'] in records:
                raise ValueError('Duplicate runtime ID')
            records[record['id']] = record
    rows, file_hashes = [], {}
    errors = []
    authoring = []
    for path in sorted(root.rglob('*')):
        if path.is_file() and (path.suffix == '.blend' or path.name.endswith(('.kit.json', '.modules.json'))):
            relative = path.relative_to(repository).as_posix()
            file_hashes[relative] = sha(path)
            authoring.append({'path': relative, 'sha256': file_hashes[relative], 'bytes': path.stat().st_size})
    for aid, (path, definition) in sorted(catalog.definitions.items()):
        relative = path.relative_to(repository).as_posix()
        if not any(relative.startswith(f'assets/source/{domain}/') for domain in DOMAINS):
            continue
        source = catalog.source(aid)
        row = {'id': aid, 'type': definition['type'], 'owner': owner(relative),
               'definition': relative, 'source': source.relative_to(repository).as_posix(),
               'authoredFormat': source.suffix.lower(),
               'sourceStatus': 'present' if source.is_file() else 'absent',
               'lods': definition.get('lods', []), 'sockets': definition.get('sockets', []),
               'collisionReference': definition.get('collisionReference'),
               'mechanicalParts': definition.get('mechanicalParts', []),
               'materialReferences': definition.get('materialReferences', []),
               'scale': definition.get('scale', 1),
               'visualAcceptance': 'unqualified-at-this-source',
               'compile': runtime_record(records.get(aid), runtime) if runtime else {'status': 'not-observed'}}
        material = None
        file_hashes[relative] = sha(path)
        if source.is_file():
            for item in (path, source):
                file_hashes[item.relative_to(repository).as_posix()] = sha(item)
            row['sourceSha256'] = sha(source)
            if definition['type'] == 'material':
                material = json.loads(source.read_text(encoding='utf-8-sig'))
                row['material'] = material
            if definition['type'] == 'texture':
                row['textureSettings'] = {k: v for k, v in definition.items() if k.startswith('texture')}
            if definition['type'] in ('mesh', 'skin'):
                try:
                    if definition.get('skinReference') or definition['type'] == 'skin':
                        document = glb_document(source)
                        primitives = [p for mesh in document.get('meshes', []) for p in mesh['primitives']]
                        row['geometry'] = {'path': 'separate-skeletal-contract',
                                           'primitiveChannels': [sorted(p['attributes']) for p in primitives],
                                           'skins': len(document.get('skins', [])),
                                           'clips': [a.get('name') for a in document.get('animations', [])]}
                    else:
                        geometry = read_geometry(source, root, definition)
                        row['geometry'] = {**geometry_stats(geometry), 'path': 'static-contract-read',
                                           'primitiveChannels': [sorted(p['attributes']) for p in geometry['primitives']],
                                           'pivotConvention': 'identity-nodes-engine-local-vertices'}
                        for item in geometry['files']:
                            file_hashes[item.relative_to(repository).as_posix()] = sha(item)
                except (ValueError, KeyError, OSError) as error:
                    row['geometry'] = {'error': str(error)}
                    errors.append({'id': aid, 'error': str(error)})
        else:
            errors.append({'id': aid, 'error': 'Missing source'})
        master = saved_master(source, root)
        row['blendMaster'] = master.relative_to(repository).as_posix() if master else None
        if master:
            file_hashes[row['blendMaster']] = sha(master)
        row['sourceMaturity'] = 'blender-backed' if master else 'present-without-bound-blend-master' if source.is_file() else 'absent'
        row['references'] = references(definition, material)
        missing = [ref for ref in row['references'] if ref not in catalog.definitions]
        if missing:
            errors.append({'id': aid, 'missingReferences': missing})
        row['missingReferences'] = missing
        rows.append(row)
    # A deterministic ledger signs file bytes; source identity does not imply acceptance.
    files = dict(sorted(file_hashes.items()))
    signature = hashlib.sha256(json.dumps(files, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    families = {}
    for label, prefix, count in (('buildings', 'building.directorate.', 13),
                                  ('units', 'unit.directorate.', 7),
                                  ('terrain', 'material.world.terrain.', 8),
                                  ('deposits', 'mesh.world.resource.', 4)):
        members = [r['id'] for r in rows if r['id'].startswith(prefix)
                   and '.' not in r['id'][len(prefix):]
                   and not r['id'].endswith('_lod1')]
        families[label] = members
        if len(members) != count:
            errors.append({'family': label, 'expected': count, 'observed': len(members)})
    return {'schemaVersion': 1, 'filesSha256': files, 'sourceSignatureSha256': signature,
            'sourceFileOwners': {path: owner(path) for path in files},
            'authoringFiles': authoring,
            'primaryFamilies': families,
            'countsByType': dict(sorted(Counter(r['type'] for r in rows).items())),
            'countsByOwner': dict(sorted(Counter(r['owner'] for r in rows).items())),
            'errors': errors, 'assets': rows,
            'acceptance': {'art': 'pending', 'nativeRenderer': 'pending', 'performance': 'pending',
                           'installedPackage': 'pending'}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--runtime', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    repo = args.repository.resolve()
    output = contained(repo, args.output.resolve())
    if output.is_relative_to(repo / 'assets') or output.exists():
        raise ValueError('Use a fresh report outside asset source/runtime trees')
    report = inventory(repo, args.runtime)
    report['repositorySha'] = subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip()
    write_json(output, report)
    print(json.dumps({'assets': len(report['assets']), 'errors': len(report['errors']),
                      'sourceSignatureSha256': report['sourceSignatureSha256']}))
    raise SystemExit(bool(report['errors']))


if __name__ == '__main__':
    main()
