"""Compare full qualified catalogs without widening the preserved prop scope."""
import argparse
import hashlib
import json
from pathlib import Path

def require(value, message):
    if not value:
        raise ValueError(message)

parser = argparse.ArgumentParser()
parser.add_argument('--baseline', required=True, type=Path)
parser.add_argument('--candidate', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
args = parser.parse_args()
require(not args.output.exists(), 'Use a fresh comparison output')
paths = [root / 'manifest.json' for root in (args.baseline, args.candidate)]
raw = [path.read_bytes() for path in paths]
records = [{entry['id']: entry for entry in json.loads(data.decode('utf-8-sig'))['assets']} for data in raw]
a, b = records
require(a.keys() == b.keys(), 'Full runtime asset roster changed')
fixture = json.loads(Path(__file__).with_name('fixture.json').read_text(encoding='utf-8'))
scoped = {aid for family in fixture['families'] for aid in family['assets']}
require(len(scoped) == 44 and scoped.issubset(a), 'Incomplete prop scope')
changed = []
for aid, before in a.items():
    after = b[aid]
    require(before['type'] == after['type'], 'Runtime asset type changed: ' + aid)
    for key in ('dependencies', 'materialReferences', 'textureReferences', 'lods', 'collisionReference', 'sockets', 'mechanicalParts'):
        require(before.get(key) == after.get(key), 'Runtime metadata changed: ' + aid + '/' + key)
    if before['runtimeHash'] != after['runtimeHash']:
        require(aid in scoped, 'Unrelated runtime asset changed: ' + aid)
        changed.append({'id': aid, 'baselineHash': before['runtimeHash'], 'candidateHash': after['runtimeHash'],
            'baselineBytes': before['runtimeBytes'], 'candidateBytes': after['runtimeBytes']})
report = {'schemaVersion': 1, 'success': True, 'assetCount': len(a), 'scopedVisualMeshes': len(scoped),
    'manifestSha256': [hashlib.sha256(data).hexdigest() for data in raw], 'changedRecords': changed,
    'unrelatedRuntimeChanges': 0, 'metadataPreserved': True,
    'totalContainerBytes': [sum(x['runtimeBytes'] for x in records.values()) for records in (a, b)],
    'textureContainerBytes': [sum(x['runtimeBytes'] for x in records.values() if x['type'].lower() == 'texture') for records in (a, b)],
    'sourceLessIntegrity': 'retain separate canonical compiler validation reports',
    'typedGeometryComparison': 'retain separate native family qualification', 'performanceAcceptance': 'pending'}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(f'Full catalogs match outside {len(scoped)} scoped props; {len(changed)} prop containers differ')
