"""Aggregate completed family runs while preserving original failed attempt evidence."""
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, fixture_selection, require, sha, write_json
FAMILIES = ('vehicle_factory','power_plant','barracks','radar')

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def tree(path):
    return {p.relative_to(path).as_posix(): sha(p) for p in path.rglob('*') if p.is_file()}

def aggregate(output, locations):
    require(output.is_relative_to(ROOT / 'artifacts') and not output.exists(), 'Use fresh contained artifacts')
    require(set(locations) == set(FAMILIES), 'Exactly four completed family inputs required')
    catalog = Catalog(ROOT / 'assets/source')
    require(subprocess.run(['git','diff','--quiet','origin/master','--','assets/source'], cwd=ROOT).returncode == 0, 'Canonical sources differ from audited master')
    require(not subprocess.check_output(['git','ls-files','--others','--exclude-standard','assets/source'], cwd=ROOT).strip(), 'Untracked canonical sources found')
    records = []
    for family in FAMILIES:
        folder = locations[family]
        ids, _ = fixture_selection(catalog, Path(__file__).with_name(family + '.json'), [])
        first = read(folder / 'first/roundtrip-report.json')
        require(first['success'] and first['sourceSignature'] == catalog.signature(ids)[0], 'Current source signature differs')
        for relative, digest in first['sourceHashes'].items():
            require(sha(catalog.root / relative) == digest, 'Source changed: ' + relative)
        for phase in ('first','second','master','reopened','native-contracts'):
            launcher = read(folder / phase / 'report.json')
            require(launcher['success'] and launcher['toolchain']['capabilities']['version'] == '5.2.2', 'Native Blender phase failed: ' + phase)
        master = read(folder / 'master/master-report.json')
        require(master['success'] and master['organizedMasterReopened'] and master['savedVertexEditReopenedAndExported'], 'Saved edit proof missing')
        native = read(folder / 'native-contracts/native-contracts.json')
        sockets = sum(len(catalog.definitions[aid][1].get('sockets', [])) for aid in ids)
        require(native['success'] and native['nativeBlender'] and native['syntheticTwoSlotTangentPositive'], 'Native contracts incomplete')
        require(len(native['cases']) == (19 if sockets else 17) and all(x['rejected'] for x in native['cases']), 'Native rejection coverage incomplete')
        not_applicable = native.get('notApplicable', [])
        require({x['name'] for x in not_applicable} == (set() if sockets else {'missing socket','moved socket','rotated socket'}), 'Unjustified missing native case')
        for variant in ('baseline','first','second','reopened','edited'):
            for kind in ('compile','source-less'):
                result = read(folder / (variant + '-' + kind + '.json'))
                require(result['success'] and result['assetCount'] == 20 and result['meshCount'] == 11, 'Compiler/integrity failure: ' + family + '/' + variant)
        for variant in ('second','reopened'):
            for kind in ('source','runtime'):
                require(tree(folder / 'first' / kind) == tree(folder / variant / kind), 'Export/runtime bytes differ')
        records.append({'family': family, 'originalOutputRoot': str(folder.relative_to(ROOT)),
            'sourceSignature': first['sourceSignature'], 'masterSha256': sha(folder / 'master/authoring.blend'),
            'nativeRejectedCases': len(native['cases']), 'notApplicable': not_applicable,
            'compiledSourceLessCatalogs': 5, 'deterministicExportsAndRuntime': True})
    output.mkdir(parents=True)
    for family in FAMILIES:
        shutil.copytree(locations[family], output / family)
        require(not (output / family / 'runtime-comparison.json').exists(), 'Input already contains a typed comparison; use unaggregated inputs')
    write_json(output / 'qualification.json', {'schemaVersion': 1,
        'sourceSha': subprocess.check_output(['git','rev-parse','HEAD'], cwd=ROOT, text=True).strip(),
        'sourceShaMeaning': 'Aggregation source; original producing identities remain in the unmodified launcher reports.',
        'families': records, 'productionSourceUnchanged': True, 'typedCompiledContracts': 'pending',
        'productionPromotion': 'blocked-render-qualification', 'humanVisualApproval': 'pending'})

if __name__ == '__main__':
    aggregate(Path(sys.argv[1]).resolve(), {x.split('=',1)[0]: Path(x.split('=',1)[1]).resolve() for x in sys.argv[2:]})
