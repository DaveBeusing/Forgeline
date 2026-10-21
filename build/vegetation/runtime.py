"""Build an isolated complete source catalog using only the eight retained visual bindings."""
import argparse
import copy
import json
import shutil
import sys
from pathlib import Path
from urllib.parse import unquote
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog, contained, require, sha, write_json
from retained import TARGET, validate


def create_source(output):
    validate()
    require(not output.exists(), 'Use a fresh complete source output')
    catalog = Catalog(ROOT / 'assets/source')
    files = set()
    for aid, (definition, metadata) in catalog.definitions.items():
        source = catalog.source(aid)
        files.update((definition, source))
        if source.suffix == '.gltf':
            gltf = json.loads(source.read_text(encoding='utf-8-sig'))
            for buffer in gltf.get('buffers', []):
                uri = buffer.get('uri', '')
                if uri and not uri.startswith('data:'):
                    require('://' not in uri, 'Network buffer URI is unsupported')
                    files.add(contained(catalog.root, source.parent / unquote(uri)))
    before = {p.relative_to(catalog.root).as_posix(): sha(p) for p in sorted(files)}
    for path in sorted(files):
        target = output / path.relative_to(catalog.root)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
    lineage = json.loads((TARGET / 'lineage.json').read_text())
    changed = []
    for entry in lineage['exports']:
        aid = entry['id']
        path, definition = catalog.definitions[aid]
        relative = path.relative_to(catalog.root)
        target = Path('_vegetation') / entry['source']
        (output / relative.parent / target).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(TARGET / entry['source'], output / relative.parent / target)
        updated = copy.deepcopy(definition)
        updated['source'] = target.as_posix()
        write_json(output / relative, updated)
        changed.append(aid)
    candidate = Catalog(output)
    require(set(candidate.definitions) == set(catalog.definitions), 'Complete catalog identity changed')
    for aid, (_, original) in catalog.definitions.items():
        updated = copy.deepcopy(candidate.definitions[aid][1])
        if aid in changed:
            updated['source'] = original['source']
        require(original == updated, 'Unowned metadata changed')
    require(before == {p.relative_to(catalog.root).as_posix(): sha(p) for p in sorted(files)}, 'Canonical source bytes changed')
    write_json(output.parent / (output.name + '-report.json'), {'schemaVersion': 1, 'success': True, 'assetCount': len(catalog.definitions), 'changedBindings': changed, 'canonicalFilesUnchanged': True, 'canonicalFileHashes': before, 'candidateDefinitions': len(candidate.definitions)})


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    create_source(parser.parse_args().output.resolve())
