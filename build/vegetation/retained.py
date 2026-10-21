"""Retain source-bound vegetation masters and qualify compiler-only deployment."""
import argparse
import copy
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, compare_geometry, fixture_selection, read_geometry, require, sha, write_json
from geometry import refine
FIXTURE = Path(__file__).with_name('fixture.json')
TARGET = ROOT / 'assets/staged/vegetation'
NAMES = ('conifer', 'broadleaf', 'scrub', 'grass_clump')


def signature(catalog, ids):
    values = {p.relative_to(catalog.root).as_posix(): hashlib.sha256(p.read_bytes().replace(b'\r\n', b'\n') if p.suffix in ('.json', '.gltf') else p.read_bytes()).hexdigest() for p in catalog.files(ids)}
    return hashlib.sha256(json.dumps(values, sort_keys=True).encode()).hexdigest()


def aggregate(root):
    canonical = Catalog(ROOT / 'assets/source')
    ids, families = fixture_selection(canonical, FIXTURE, [])
    catalog = Catalog(root / 'candidate-source')
    require(json.loads((root / 'native-batch.json').read_text())['success'], 'Native run incomplete')
    require(json.loads((root / 'candidate-report.json').read_text())['canonicalSignature'] == canonical.signature(ids)[0], 'Canonical input drift')
    for variant in ('baseline', 'first', 'second', 'reopened', 'edited'):
        destination = root / variant / 'source'
        require(not destination.exists(), 'Use fresh aggregate source')
        (canonical if variant == 'baseline' else catalog).copy_to(ids, destination)
        if variant == 'baseline':
            continue
        for family in families:
            name = family['root'].split('.')[-1]
            proof = json.loads((root / name / 'native-contracts/native-contracts.json').read_text())
            require(proof['success'] and proof['unchangedReopenAccepted'] and set(proof['rejected']) == {'unbaked transform','changed envelope','modifier','animation','material slots','unexpected object'}, 'Missing native rejection proof')
            edit = json.loads((root / name / 'edit-check/master-report.json').read_text())
            require(edit['success'] and edit['savedVertexEditReopenedAndExported'], 'Missing real editable-master proof')
            exported = Catalog(root / name / ('edit-check/edit-source' if variant == 'edited' else variant + '/source'))
            if variant != 'edited':
                report = json.loads((root / name / variant / 'roundtrip-report.json').read_text())
                require(report['success'] and report['candidateSourceFloatsExact'], 'Native readback incomplete')
            for aid in family['assets']:
                path, definition = catalog.definitions[aid]
                relative = path.relative_to(catalog.root)
                filename = Path('_native') / (aid + '.glb')
                (destination / relative.parent / filename).parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(exported.source(aid), destination / relative.parent / filename)
                updated = copy.deepcopy(definition)
                updated['source'] = filename.as_posix()
                write_json(destination / relative, updated)
    write_json(root / 'inputs.json', {'sourceSha': subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True).strip(), 'portableCanonicalSignature': signature(canonical, ids)})


def qualify(root):
    for variant in ('baseline', 'first', 'second', 'reopened', 'edited'):
        for report in ('compile.json', 'source-less.json'):
            require(json.loads((root / variant / report).read_text(encoding='utf-8-sig'))['success'], 'Compiler validation failed')
    for variant in ('second', 'reopened'):
        require((root / 'first/runtime/manifest.json').read_bytes() == (root / variant / 'runtime/manifest.json').read_bytes(), 'Nondeterministic runtime manifest')
        first = Catalog(root / 'first/source')
        other = Catalog(root / variant / 'source')
        ids, _ = fixture_selection(first, FIXTURE, [])
        require(first.signature(ids)[0] == other.signature(ids)[0], 'Nondeterministic native source')
        manifest = json.loads((root / 'first/runtime/manifest.json').read_text(encoding='utf-8-sig'))
        for asset in manifest['assets']:
            filename = asset['runtimePath']
            require(sha(root / 'first/runtime' / filename) == sha(root / variant / 'runtime' / filename), 'Nondeterministic runtime payload')
    write_json(root / 'qualification.json', {'schemaVersion': 1, 'success': True, 'sourceSha': json.loads((root / 'inputs.json').read_text())['sourceSha'], 'deterministicNativeSourceAndRuntime': True, 'sourceLessIntegrity': True, 'nativeSavedEditCompiled': True, 'physicalPerformance': 'pending', 'humanArtApproval': 'pending', 'productionPromotion': 'blocked-pending-render-qualification'})


def retain(root):
    require(json.loads((root / 'qualification.json').read_text())['success'], 'Qualify native outputs first')
    require(not TARGET.exists(), 'Existing candidate must be explicitly reviewed')
    TARGET.mkdir(parents=True)
    catalog = Catalog(ROOT / 'assets/source')
    ids, families = fixture_selection(catalog, FIXTURE, [])
    candidate = Catalog(root / 'first/source')
    masters, exports = [], []
    for family in families:
        name = family['root'].split('.')[-1]
        filename = name + '.blend'
        master = root / name / 'editable.blend'
        if not master.exists():
            master = root / name / 'first/authoring.blend'
        shutil.copyfile(master, TARGET / filename)
        masters.append({'path': filename, 'sha256': sha(TARGET / filename)})
        for aid in family['assets']:
            filename = aid + '.glb'
            shutil.copyfile(candidate.source(aid), TARGET / filename)
            exports.append({'id': aid, 'source': filename, 'sha256': sha(TARGET / filename), 'canonicalDefinition': catalog.definitions[aid][0].relative_to(catalog.root).as_posix()})
    filename = 'vegetation_inspection.blend'
    shutil.copyfile(root / filename, TARGET / filename)
    masters.append({'path': filename, 'sha256': sha(TARGET / filename)})
    write_json(TARGET / 'lineage.json', {'schemaVersion': 1, 'status': 'staged-not-promoted', 'sourceSha': json.loads((root / 'qualification.json').read_text())['sourceSha'], 'canonicalBaseSha': json.loads((root / 'qualification.json').read_text())['sourceSha'], 'nativeProducerScripts': json.loads((root / 'native-batch.json').read_text())['scripts'], 'editorialProducerSha256': sha(Path(__file__).with_name('organize_saved.py')), 'anatomyProducerSha256': sha(Path(__file__).with_name('anatomy.py')), 'sourceWorkingTreeClean': not bool(subprocess.check_output(['git','-C',str(ROOT),'status','--porcelain'],text=True).strip()), 'portableCanonicalSignature': signature(catalog, ids), 'fixtureSha256': sha(FIXTURE), 'masters': masters, 'exports': exports, 'blenderVersion': '5.2.2', 'humanArtApproval': 'pending', 'newTextures': 0, 'newMaterials': 0, 'changes': json.loads((root / 'candidate-report.json').read_text())['changes']})
    (TARGET / '.gitattributes').write_text('*.blend binary\n*.glb binary\n*.json text eol=lf\n')
    validate()


def validate():
    catalog = Catalog(ROOT / 'assets/source')
    ids, families = fixture_selection(catalog, FIXTURE, [])
    lineage = json.loads((TARGET / 'lineage.json').read_text())
    require(lineage['status'] == 'staged-not-promoted' and lineage['portableCanonicalSignature'] == signature(catalog, ids), 'Source drift or invalid promotion claim')
    require(lineage['fixtureSha256'] == sha(FIXTURE), 'Fixture drift')
    require(len(lineage['masters']) == 5 and {m['path'] for m in lineage['masters']} == {n+'.blend' for n in NAMES} | {'vegetation_inspection.blend'}, 'Missing or duplicate masters')
    for entry in lineage['masters']:
        require(sha(TARGET / entry['path']) == entry['sha256'], 'Master hash changed')
    owned = {aid for f in families for aid in f['assets']}
    require(len(lineage['exports']) == 8 and {e['id'] for e in lineage['exports']} == owned, 'Incomplete export set')
    for entry in lineage['exports']:
        aid = entry['id']
        require(entry['source'] == aid + '.glb' and sha(TARGET / entry['source']) == entry['sha256'], 'Export path/hash changed')
        require(entry['canonicalDefinition'] == catalog.definitions[aid][0].relative_to(catalog.root).as_posix(), 'Definition owner changed')
        original = read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
        actual = read_geometry(TARGET / entry['source'], TARGET, catalog.definitions[aid][1])
        if aid.startswith('mesh.world.vegetation.conifer'):
            expected = copy.deepcopy(original)
            uv = expected['primitives'][0]['attributes']['TEXCOORD_0']
            lo, hi = [min(x[i] for x in uv) for i in range(2)], [max(x[i] for x in uv) for i in range(2)]
            sides = 6 if aid.endswith('_lod1') else 10
            for v in expected['primitives'][0]['indices'][:sides*6]:
                uv[v] = tuple(.518 + (original['primitives'][0]['attributes']['TEXCOORD_0'][v][i]-lo[i])/(hi[i]-lo[i])*.214 for i in range(2))
            compare_geometry(expected, actual)
        elif aid.split('.')[3].removesuffix('_lod1') in ('scrub', 'grass_clump'):
            name = aid.split('.')[3].removesuffix('_lod1')
            compare_geometry(refine(name, original, aid.endswith('_lod1')), actual)
        else:
            require(max(compare_geometry(original, actual).values()) == 0, 'Integrated geometry was reauthored')
    print('Vegetation: four rooted native masters, shared packed inspection and eight bounded exports validated')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['prepare','qualify','retain','validate'])
    parser.add_argument('output', nargs='?', type=Path)
    args = parser.parse_args()
    if args.action == 'validate':
        validate()
    else:
        require(args.output is not None, 'Output root required')
        {'prepare': aggregate, 'qualify': qualify, 'retain': retain}[args.action](args.output.resolve())
