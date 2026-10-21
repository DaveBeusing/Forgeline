"""Run mineral imports in independent factory scenes in the pinned Blender process."""
import json
import runpy
import sys
from pathlib import Path

import bpy

request = json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig'))
root = Path(request['inputRoot']).resolve().parents[1]
sys.path.insert(0, str(root / 'build/resource-deposits'))
import author
from asset_contract import sha, write_json

output = Path(request['outputRoot'])
fixture = root / 'build/resource-deposits/fixture.json'
names = ('ferrous_ore', 'silicates', 'volatiles', 'rare_elements')
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
        arguments=[str(family / 'first/authoring.blend'), str(fixture), 'mesh.world.resource.' + name]))
    sys.argv = [str(root / 'build/resource-deposits/native_contract.py'), '--request', str(contract_request)]
    runpy.run_path(sys.argv[0], run_name='__main__')
write_json(output / 'native-batch.json', {'schemaVersion': 1, 'success': True,
    'freshFactoryScenePerImport': True, 'families': names, 'blenderVersion': bpy.app.version_string,
    'scripts': {name: sha(root / 'build/resource-deposits' / name)
                for name in ('author.py', 'native_contract.py', 'mineral_contract.py', 'author_batch.py')}})
