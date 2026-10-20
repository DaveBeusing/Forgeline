"""Retain qualified masters and exports outside the production source ownership hold."""
import hashlib
import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def retain(qualified):
    summary = json.loads((qualified / 'qualification.json').read_text(encoding='utf-8-sig'))
    for name in ('savedAndReopenedBlenderMasters', 'deterministicSourceAndRuntime', 'sourceLessIntegrity', 'typedCompiledComparison'):
        if summary[name] != 'passed':
            raise ValueError('Unqualified master: ' + name)
    target = ROOT / 'assets/staged/industrial-plants'
    target.mkdir(parents=True, exist_ok=False)
    for family in ('fuel_refinery', 'ammunition_plant'):
        first = qualified / family / 'first'
        report = json.loads((first / 'roundtrip-report.json').read_text(encoding='utf-8'))
        fixture = Path(__file__).with_name(family + '.json')
        assets = json.loads(fixture.read_text(encoding='utf-8'))['families'][0]['assets']
        folder = target / family
        folder.mkdir()
        shutil.copyfile(first / 'authoring.blend', folder / (family + '.blend'))
        bindings = []
        for definition_file in sorted((first / 'source').rglob('*.asset.json')):
            definition = json.loads(definition_file.read_text(encoding='utf-8-sig'))
            if definition['id'] not in assets:
                continue
            source = definition_file.parent / definition['source']
            filename = definition['id'] + '.glb'
            shutil.copyfile(source, folder / filename)
            bindings.append({'id': definition['id'], 'source': filename,
                             'canonicalDefinition': definition_file.relative_to(first / 'source').as_posix(),
                             'sha256': digest(folder / filename)})
        if len(bindings) != 11:
            raise ValueError('Incomplete retained family')
        lineage = {
            'schemaVersion': 1, 'status': 'staged-not-promoted',
            'sourceSha': summary['sourceSha'], 'sourceSignature': report['sourceSignature'],
            'fixtureSha256': digest(fixture), 'blenderVersion': '5.2.2',
            'master': family + '.blend', 'masterSha256': digest(folder / (family + '.blend')),
            'exports': bindings,
            'canonicalSourcesUnchanged': True,
            'promotionGate': 'Current render qualification, source ownership release, native/package/performance and human visual acceptance required.',
        }
        (folder / 'lineage.json').write_text(json.dumps(lineage, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    retain(Path(sys.argv[1]).resolve())
