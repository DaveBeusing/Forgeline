"""Retain and verify mineral preservation masters independently of production promotion."""
import hashlib
import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, compare_geometry, fixture_selection, read_geometry, require, sha, write_json

FIXTURE = Path(__file__).with_name('fixture.json')
TARGET = ROOT / 'assets/staged/resource-deposits'
NAMES = ('ferrous_ore', 'silicates', 'volatiles', 'rare_elements')


def portable_signature(catalog, ids):
    hashes = {}
    for path in catalog.files(ids):
        content = path.read_bytes()
        if path.suffix.lower() in ('.json', '.gltf'):
            content = content.replace(b'\r\n', b'\n')
        hashes[path.relative_to(catalog.root).as_posix()] = hashlib.sha256(content).hexdigest()
    return hashlib.sha256(json.dumps(hashes, sort_keys=True).encode()).hexdigest()


def retain(output):
    qualification = json.loads((output / 'qualification.json').read_text(encoding='utf-8-sig'))
    require(all(qualification.get(k) is True for k in ('success', 'deterministicSourceAndRuntime',
                'sourceLessIntegrity', 'typedCompiledComparison', 'canonicalSourcesUnchanged')),
            'Native qualification is incomplete')
    require(not TARGET.exists(), 'Retained masters are never overwritten')
    catalog = Catalog(ROOT / 'assets/source')
    for name in NAMES:
        ids, families = fixture_selection(catalog, FIXTURE, ['mesh.world.resource.' + name])
        first = output / name / 'first'
        report = json.loads((first / 'roundtrip-report.json').read_text(encoding='utf-8'))
        require(report['success'] and report['sourceSignature'] == portable_signature(catalog, ids), 'Source changed')
        target = TARGET / name
        target.mkdir(parents=True, exist_ok=False)
        masters = []
        for source, filename in [('authoring.blend', name + '.blend'), ('inspection.blend', name + '_inspection.blend')]:
            shutil.copyfile(first / source, target / filename)
            masters.append({'path': filename, 'sha256': sha(target / filename)})
        exported = Catalog(first / 'source')
        bindings = []
        for aid in families[0]['assets']:
            path, _ = catalog.definitions[aid]
            filename = aid + '.glb'
            shutil.copyfile(exported.source(aid), target / filename)
            bindings.append({'id': aid, 'source': filename, 'sha256': sha(target / filename),
                             'canonicalDefinition': path.relative_to(catalog.root).as_posix()})
        write_json(target / 'lineage.json', {'schemaVersion': 1, 'status': 'staged-not-promoted',
                   'sourceSha': qualification['sourceSha'], 'sourceSignature': report['sourceSignature'],
                   'portableSourceSignature': portable_signature(catalog, ids), 'fixtureSha256': sha(FIXTURE),
                   'blenderVersion': '5.2.2', 'masters': masters, 'exports': bindings,
                   'canonicalSourcesUnchanged': True, 'humanArtApproval': 'pending'})


def refresh_native(output):
    qualification = json.loads((output / 'qualification.json').read_text(encoding='utf-8-sig'))
    require(all(qualification.get(k) is True for k in ('success', 'deterministicSourceAndRuntime',
                'sourceLessIntegrity', 'typedCompiledComparison', 'canonicalSourcesUnchanged')),
            'Native qualification is incomplete')
    catalog = Catalog(ROOT / 'assets/source')
    pending = []
    for name in NAMES:
        ids, _ = fixture_selection(catalog, FIXTURE, ['mesh.world.resource.' + name])
        target = TARGET / name
        lineage = json.loads((target / 'lineage.json').read_text(encoding='utf-8'))
        signature = portable_signature(catalog, ids)
        require(lineage['status'] == 'review-bound-not-accepted' and
                lineage['boundPortableSourceSignature'] == signature, 'Bound source changed')
        first = output / name / 'first'
        report = json.loads((first / 'roundtrip-report.json').read_text(encoding='utf-8'))
        require(report['success'] and report['sourceSignature'] == signature, 'Native signature is not portable')
        exports = {entry['id']: entry['exportSha256'] for entry in report['meshes']}
        require(exports == {entry['id']: entry['sha256'] for entry in lineage['exports']},
                'Native refresh cannot change the retained exports')
        for master in lineage['masters']:
            require(sha(target / master['path']) == master['sha256'], 'Existing native edit would be overwritten')
        replacements = [('authoring.blend', name + '.blend'), ('inspection.blend', name + '_inspection.blend')]
        lineage['previousNativeMasters'] = lineage['masters']
        lineage['masters'] = [{'path': filename, 'sha256': sha(first / source)} for source, filename in replacements]
        lineage['authoringSourceSha'] = qualification['sourceSha']
        lineage['nativeContractSourceSignature'] = signature
        lineage['nativeContractSignaturePolicy'] = 'lf-json-v1'
        pending.append((target, first, replacements, lineage))
    for target, first, replacements, lineage in pending:
        for source, filename in replacements:
            shutil.copyfile(first / source, target / filename)
        write_json(target / 'lineage.json', lineage)


def validate():
    if (ROOT / 'assets/source/world/authoring/resource_clusters/sources.json').is_file():
        # New topology has its own saved-master/export contract. Historical masters
        # remain hash-checked by that contract, without claiming their old signatures
        # qualify the replacement geometry.
        from cluster_contract import validate as validate_clusters
        validate_clusters()
        if (ROOT / 'assets/source/world/authoring/resource_surfaces/sources.json').is_file():
            from surface_contract import validate as validate_surfaces
            validate_surfaces(ROOT / 'assets/source', ROOT / 'assets/source')
        return
    catalog = Catalog(ROOT / 'assets/source')
    for name in NAMES:
        ids, families = fixture_selection(catalog, FIXTURE, ['mesh.world.resource.' + name])
        target = TARGET / name
        lineage = json.loads((target / 'lineage.json').read_text(encoding='utf-8'))
        require(lineage['status'] in ('staged-not-promoted', 'review-bound-not-accepted'), 'Unexpected acceptance claim')
        if lineage['status'] == 'staged-not-promoted':
            require(lineage['portableSourceSignature'] == portable_signature(catalog, ids), 'Canonical source changed')
        else:
            require(lineage['boundPortableSourceSignature'] == portable_signature(catalog, ids), 'Bound source changed')
            require(lineage.get('nativeContractSignaturePolicy') == 'lf-json-v1' and
                    lineage.get('nativeContractSourceSignature') == lineage['boundPortableSourceSignature'],
                    'Native source contract needs a qualified refresh')
        require(lineage['fixtureSha256'] == sha(FIXTURE), 'Fixture changed')
        require({x['path'] for x in lineage['masters']} == {name + '.blend', name + '_inspection.blend'}
                and len(lineage['masters']) == 2, 'Incomplete masters')
        for master in lineage['masters']:
            path = target / master['path']
            require(master['sha256'] == sha(path), 'Native master changed')
        expected = set(families[0]['assets'])
        require({x['id'] for x in lineage['exports']} == expected and len(lineage['exports']) == len(expected),
                'Incomplete or duplicated exports')
        for entry in lineage['exports']:
            path, definition = catalog.definitions[entry['id']]
            require(entry['canonicalDefinition'] == path.relative_to(catalog.root).as_posix(), 'Definition owner changed')
            require(entry['source'] == entry['id'] + '.glb', 'Unexpected export path')
            exported = target / entry['source']
            if lineage['status'] == 'review-bound-not-accepted':
                require(definition['source'] == '../sources/blender/' + name + '/' + entry['source'], 'Binding changed')
                require(sha(catalog.source(entry['id'])) == entry['sha256'], 'Bound export changed')
            require(entry['sha256'] == sha(exported), 'Export changed')
            compare_geometry(read_geometry(catalog.source(entry['id']), catalog.root, definition),
                             read_geometry(exported, target, definition))
    print('Four mineral families: native rest/inspection masters and High/Reduced exports match current sources')


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--refresh-native':
        refresh_native(Path(sys.argv[2]).resolve())
    elif len(sys.argv) == 2:
        retain(Path(sys.argv[1]).resolve())
    else:
        require(len(sys.argv) == 1, 'Use no arguments, an initial retention root, or --refresh-native ROOT')
    validate()
