"""Reopen the checked-in native masters against fresh current source clones."""
import json
import sys
import subprocess
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import author
from asset_contract import require, sha, write_json
from retained import TARGET, validate

request = json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig'))
validate()
output = Path(request['outputRoot'])
catalog, ids, families = author.prepare(request['inputRoot'], output)
lineage = json.loads((TARGET / 'lineage.json').read_text())
rows = []
for family in families:
    name = family['root'].split('.')[-1]
    target = output / name
    target.mkdir()
    master = TARGET / (name + '.blend')
    author.export(request, catalog, family['root'], target, master)
    report = json.loads((target / 'roundtrip-report.json').read_text())
    for entry in lineage['exports']:
        if entry['id'] in family['assets']:
            row = next(r for r in report['meshes'] if r['id'] == entry['id'])
            require(row['exportSha256'] == entry['sha256'], 'Checked-in native master differs from retained export')
    rows.append({'root': family['root'], 'masterSha256': sha(master), 'retainedExportsExact': True})
write_json(output / 'retained-native.json', {'schemaVersion': 1, 'success': True, 'sourceSha': subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True).strip(), 'producerScriptSha256': sha(Path(__file__)), 'lineageSha256': sha(TARGET/'lineage.json'), 'families': rows})
