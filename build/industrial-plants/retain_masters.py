"""Retain qualified masters and exports outside the production source ownership hold."""
import hashlib
import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, fixture_selection
from lineage import portable_signature


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def retain(qualified, reviews, packed_reviews):
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
        baseline_catalog = Catalog(first / 'baseline-source')
        baseline_ids, _ = fixture_selection(baseline_catalog, fixture, [])
        folder = target / family
        folder.mkdir()
        shutil.copyfile(first / 'authoring.blend', folder / (family + '.blend'))
        review_name = 'review-fuel' if family == 'fuel_refinery' else 'review-ammunition'
        review = reviews / review_name
        review_report = json.loads((review / 'production-report.json').read_text(encoding='utf-8'))
        if not review_report['success'] or review_report['sourceSignature'] != report['sourceSignature']:
            raise ValueError('PBR review does not match the qualified source family')
        surface_master = family + '_surfaces.blend'
        packed_report = json.loads((packed_reviews / 'packed-reviews.json').read_text(encoding='utf-8'))
        packed_family = next(x for x in packed_report['families'] if x['family'] == family)
        if not packed_report['success'] or packed_family['masterSha256'] != digest(packed_reviews / surface_master):
            raise ValueError('Packed review master validation failed')
        shutil.copyfile(packed_reviews / surface_master, folder / surface_master)
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
            'portableSourceSignature': portable_signature(baseline_catalog, baseline_ids),
            'fixtureSha256': digest(fixture), 'blenderVersion': '5.2.2',
            'master': family + '.blend', 'masterSha256': digest(folder / (family + '.blend')),
            'surfaceReviewMaster': surface_master, 'surfaceReviewMasterSha256': digest(folder / surface_master),
            'exports': bindings,
            'canonicalSourcesUnchanged': True,
            'promotionGate': 'Current render qualification, source ownership release, native/package/performance and human visual acceptance required.',
        }
        (folder / 'lineage.json').write_text(json.dumps(lineage, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    retain(Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve(), Path(sys.argv[3]).resolve())
