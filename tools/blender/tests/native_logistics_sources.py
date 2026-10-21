import json
import shutil
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, fixture_selection, require, sha, write_json
from logistics import run

request = json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig'))
family, master = request['arguments']
master = Path(master)
output = Path(request['outputRoot'])
production = Catalog(request['inputRoot'])
fixture = Path(__file__).resolve().parents[1] / 'fixtures' / ('logistics_' + family + '.json')
ids, _ = fixture_selection(production, fixture, [])
signature = production.signature(ids)[0]
master_hash = sha(master)
root = 'building.directorate.' + family
critical = 'material.directorate.building.critical'
results = []
for name, expected in [('binding','source metadata changed'), ('value','Material source changed')]:
    source = output / name
    production.copy_to(production.closure(ids + [critical]), source)
    kit = Path('buildings/directorate/authoring') / (family + '.kit.json')
    (source / kit).parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(production.root / kit, source / kit)
    catalog = Catalog(source)
    if name == 'binding':
        for suffix in ('','.lod1','.lod2'):
            path = catalog.definitions[root + suffix][0]
            data = json.loads(path.read_text(encoding='utf-8-sig'))
            data['materialReferences'] = [critical]
            write_json(path,data)
    else:
        path = catalog.source('material.directorate.building.structural')
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        data['roughnessFactor'] = .123456
        write_json(path,data)
    try:
        run({'inputRoot':str(source),'outputRoot':str(output / (name+'-export')),
             'arguments':['--action','Export','--family',family,'--blend-file',str(master)]})
    except ValueError as error:
        require(expected in str(error),'Wrong source rejection gate: '+str(error))
        results.append({'case':name,'rejected':True,'error':str(error)})
    else: raise AssertionError('Changed source accepted: '+name)
require(production.signature(ids)[0] == signature and sha(master) == master_hash,'Production changed')
write_json(output / 'contracts.json',{'success':True,'family':family,'cases':results,'productionUnchanged':True})
