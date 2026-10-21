"""Retain and verify the scout preservation candidate against canonical sources."""
import hashlib
import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, compare_geometry, fixture_selection, read_geometry, require, sha, write_json

FIXTURE = Path(__file__).with_name('fixture.json')
TARGET = ROOT / 'assets/staged/scout'


def portable_signature(catalog, ids):
    hashes = {}
    for path in catalog.files(ids):
        content = path.read_bytes()
        if path.suffix.lower() in ('.json', '.gltf'):
            content = content.replace(b'\r\n', b'\n')
        hashes[path.relative_to(catalog.root).as_posix()] = hashlib.sha256(content).hexdigest()
    return hashlib.sha256(json.dumps(hashes, sort_keys=True).encode()).hexdigest()


def retain(root):
    qualification = json.loads((root / 'qualification.json').read_text(encoding='utf-8-sig'))
    require(qualification['success'] and qualification['deterministicSourceAndRuntime']
            and qualification['sourceLessIntegrity'] and qualification['typedCompiledComparison'],
            'Native qualification is incomplete')
    catalog = Catalog(ROOT / 'assets/source')
    ids, families = fixture_selection(catalog, FIXTURE, [])
    first = root / 'first'
    report = json.loads((first / 'roundtrip-report.json').read_text(encoding='utf-8'))
    require(report['success'] and report['sourceSignature'] == catalog.signature(ids)[0], 'Source changed')
    TARGET.mkdir(parents=True, exist_ok=False)
    files = []
    for source, target in [('authoring.blend', 'scout.blend'), ('inspection.blend', 'scout_inspection.blend')]:
        shutil.copyfile(first / source, TARGET / target)
        files.append({'path': target, 'sha256': sha(TARGET / target)})
    exported = Catalog(first / 'source')
    bindings = []
    for aid in families[0]['assets']:
        path, _ = catalog.definitions[aid]
        filename = aid + '.glb'
        shutil.copyfile(exported.source(aid), TARGET / filename)
        bindings.append({'id': aid, 'source': filename, 'sha256': sha(TARGET / filename),
                         'canonicalDefinition': path.relative_to(catalog.root).as_posix()})
    write_json(TARGET / 'lineage.json', {'schemaVersion': 1, 'status': 'staged-not-promoted',
               'sourceSha': qualification['sourceSha'], 'sourceSignature': report['sourceSignature'],
               'portableSourceSignature': portable_signature(catalog, ids), 'fixtureSha256': sha(FIXTURE),
               'blenderVersion': '5.2.2', 'masters': files, 'exports': bindings,
               'canonicalSourcesUnchanged': True, 'humanArtApproval': 'pending'})


def validate():
    catalog = Catalog(ROOT / 'assets/source')
    ids, families = fixture_selection(catalog, FIXTURE, [])
    lineage = json.loads((TARGET / 'lineage.json').read_text(encoding='utf-8'))
    require(lineage['status'] == 'staged-not-promoted', 'Unexpected promotion claim')
    require(lineage['portableSourceSignature'] == portable_signature(catalog, ids), 'Canonical source changed')
    require(lineage['fixtureSha256'] == sha(FIXTURE), 'Fixture changed')
    require({x['path'] for x in lineage['masters']} == {'scout.blend', 'scout_inspection.blend'}
            and len(lineage['masters']) == 2, 'Incomplete native masters')
    for master in lineage['masters']:
        require(master['sha256'] == sha(TARGET / master['path']), 'Native master changed')
    expected = set(families[0]['assets'])
    require({x['id'] for x in lineage['exports']} == expected and len(lineage['exports']) == len(expected),
            'Incomplete or duplicated exports')
    for entry in lineage['exports']:
        path, definition = catalog.definitions[entry['id']]
        require(entry['canonicalDefinition'] == path.relative_to(catalog.root).as_posix(), 'Definition owner changed')
        require(entry['source'] == entry['id'] + '.glb', 'Unexpected export path')
        exported = TARGET / entry['source']
        require(entry['sha256'] == sha(exported), 'Export hash changed')
        compare_geometry(read_geometry(catalog.source(entry['id']), catalog.root, definition),
                         read_geometry(exported, TARGET, definition))
    print('Scout: two native projects and eight geometry exports match current sources')


if __name__ == '__main__':
    if len(sys.argv) == 2:
        retain(Path(sys.argv[1]).resolve())
    validate()
