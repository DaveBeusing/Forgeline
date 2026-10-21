"""Run prop imports in independent factory scenes in the pinned Blender process."""
import json
import runpy
import sys
from pathlib import Path

import bpy

request = json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig'))
root = Path(request['inputRoot']).resolve().parents[1]
sys.path.insert(0, str(root / 'build/world-props'))
import author
author.Catalog.freeze(request['inputRoot'])
from asset_contract import sha, write_json

output = Path(request['outputRoot'])
fixture = root / 'build/world-props/fixture.json'
names = [f['root'].removeprefix('mesh.world.prop.') for f in json.loads(fixture.read_text(encoding='utf-8'))['families']]
for name in names:
    family = output / name
    for variant in ('first', 'second', 'reopened'):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        target = family / variant
        target.mkdir(parents=True, exist_ok=False)
        arguments = ['--fixtures', str(fixture), '--family', name]
        if variant == 'first':
            arguments += ['--inspect']
            if '--render' in request['arguments']:
                arguments += ['--render']
        if variant == 'reopened':
            arguments += ['--blend-file', str(family / 'first/authoring.blend')]
        author.run(dict(request, outputRoot=str(target), arguments=arguments))
    target = family / 'native-contracts'
    target.mkdir(parents=True, exist_ok=False)
    contract_request = target / 'request.json'
    write_json(contract_request, dict(request, outputRoot=str(target),
        arguments=[str(family / 'first/authoring.blend'), str(fixture), 'mesh.world.prop.' + name]))
    sys.argv = [str(root / 'build/world-props/native_contract.py'), '--request', str(contract_request)]
    runpy.run_path(sys.argv[0], run_name='__main__')
write_json(output / 'native-batch.json', {'schemaVersion': 1, 'success': True,
    'freshFactoryScenePerImport': True, 'families': names, 'blenderVersion': bpy.app.version_string,
    'scripts': {name: sha(root / 'build/world-props' / name)
                for name in ('author.py', 'native_contract.py', 'prop_contract.py', 'author_batch.py')}})
