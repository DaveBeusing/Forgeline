"""Retain measured operational masters without promoting canonical source bindings."""
import hashlib
import json
import shutil
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
sys.path.insert(0, str(ROOT / 'build/industrial-plants'))
from asset_contract import Catalog, fixture_selection, require, sha, write_json
from lineage import portable_signature
FAMILIES = ('vehicle_factory', 'power_plant', 'barracks', 'radar')

def retain(qualified, reviews, packed, organized):
    summary = json.loads((qualified / 'qualification.json').read_text(encoding='utf-8-sig'))
    require(summary['typedCompiledContracts'] == 'passed' and summary['productionSourceUnchanged'], 'Qualification incomplete')
    target = ROOT / 'assets/staged/operational-buildings'
    target.mkdir(parents=True, exist_ok=False)
    (target / '.gitattributes').write_text('*.blend binary\n*.glb binary\n*.json -text -whitespace\n', encoding='utf-8')
    component_report = json.loads((organized / 'component-masters.json').read_text(encoding='utf-8'))
    require(component_report['success'], 'Component organization not qualified')
    packed_report = json.loads((packed / 'packed-reviews.json').read_text(encoding='utf-8'))
    require(packed_report['success'], 'Packed image validation failed')
    for family in FAMILIES:
        root = qualified / family
        fixture = Path(__file__).with_name(family + '.json')
        catalog = Catalog(root / 'first/baseline-source')
        ids, families = fixture_selection(catalog, fixture, [])
        report = json.loads((root / 'first/roundtrip-report.json').read_text(encoding='utf-8'))
        review = json.loads((reviews / family / 'production-report.json').read_text(encoding='utf-8'))
        require(review['success'] and review['sourceSignature'] == report['sourceSignature'], 'Review source mismatch')
        folder = target / family
        folder.mkdir()
        master = family + '.blend'
        surface = family + '_surfaces.blend'
        component_family = next(x for x in component_report['families'] if x['family'] == family)
        require(component_family['sourceSignature'] == report['sourceSignature'], 'Organized master source mismatch')
        require(component_family['originalMasterSha256'] == sha(root / 'master/authoring.blend'), 'Organized master lineage changed')
        require(component_family['masterSha256'] == sha(organized / master), 'Organized master hash mismatch')
        require(component_family['reopened'] and component_family['exactExports'] == 10, 'Organized master export incomplete')
        shutil.copyfile(organized / master, folder / master)
        packed_family = next(x for x in packed_report['families'] if x['family'] == family)
        require(packed_family['masterSha256'] == sha(packed / surface), 'Packed master mismatch')
        shutil.copyfile(packed / surface, folder / surface)
        exports = []
        exported = Catalog(root / 'first/source')
        for aid in families[0]['assets']:
            definition_path, definition = catalog.definitions[aid]
            source_name = aid + '.glb'
            shutil.copyfile(exported.source(aid), folder / source_name)
            exports.append({'id': aid, 'source': source_name, 'sha256': sha(folder / source_name),
                'canonicalDefinition': definition_path.relative_to(catalog.root).as_posix(), 'definition': definition})
        require(len(exports) == 10, 'Incomplete family')
        write_json(folder / 'lineage.json', {'schemaVersion': 1, 'status': 'staged-not-promoted',
            'sourceSha': summary['sourceSha'], 'sourceSignature': report['sourceSignature'],
            'portableSourceSignature': portable_signature(catalog, ids), 'fixtureSha256': sha(fixture),
            'blenderVersion': '5.2.2', 'componentSelectionGroups': len(component_family['componentGroups']), 'master': master, 'masterSha256': sha(folder / master),
            'surfaceReviewMaster': surface, 'surfaceReviewMasterSha256': sha(folder / surface),
            'exports': exports, 'canonicalSourcesUnchanged': True, 'productionPromoted': False})

if __name__ == '__main__':
    retain(*(Path(p).resolve() for p in sys.argv[1:5]))
