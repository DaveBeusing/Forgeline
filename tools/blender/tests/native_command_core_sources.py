"""Reject changed source bindings and material values with the real saved master."""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, fixture_selection, require, sha, write_json
from command_core import run
from command_core_contract import ROOT, MATERIAL

request = json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig'))
output = Path(request['outputRoot'])
master, fixture = map(Path, request['arguments'])
production = Catalog(request['inputRoot'])
ids, _ = fixture_selection(production, fixture, [])
signature = production.signature(ids)[0]
master_hash = sha(master)
results = []
for name, expected in [('binding', 'source metadata changed'), ('value', 'Material source changed')]:
    source = output / name
    production.copy_to(ids, source)
    catalog = Catalog(source)
    if name == 'binding':
        for suffix in ('', '.lod1', '.lod2'):
            path = catalog.definitions[ROOT + suffix][0]
            data = json.loads(path.read_text(encoding='utf-8-sig'))
            data['materialReferences'] = [MATERIAL + '.critical']
            write_json(path, data)
    else:
        path = catalog.source(MATERIAL)
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        data['roughnessFactor'] = .5
        write_json(path, data)
    try:
        run({'inputRoot': str(source), 'outputRoot': str(output / (name + '-export')),
             'arguments': ['--action', 'Export', '--fixtures', str(fixture), '--blend-file', str(master)]})
    except ValueError as error:
        require(expected in str(error), 'Wrong source rejection gate: ' + str(error))
        results.append({'case': name, 'rejected': True, 'error': str(error)})
    else:
        raise AssertionError('Changed source was accepted: ' + name)
require(production.signature(ids)[0] == signature and sha(master) == master_hash, 'Production source changed')
write_json(output / 'source-contracts.json', {'success': True, 'cases': results, 'productionUnchanged': True})
