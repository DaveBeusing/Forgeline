"""Verify checked-in master/export lineage against current authoritative sources."""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, compare_geometry, fixture_selection, read_geometry, require, sha
sys.path.insert(0, str(ROOT / 'build/industrial-plants'))
from lineage import portable_signature


def validate():
    catalog = Catalog(ROOT / 'assets/source')
    for family in ('vehicle_factory', 'power_plant', 'barracks', 'radar'):
        fixture = Path(__file__).with_name(family + '.json')
        ids, families = fixture_selection(catalog, fixture, [])
        folder = ROOT / 'assets/staged/operational-buildings' / family
        lineage = json.loads((folder / 'lineage.json').read_text(encoding='utf-8'))
        require(lineage['status'] == 'staged-not-promoted', 'Unexpected production claim')
        require(lineage['portableSourceSignature'] == portable_signature(catalog, ids), 'Authoritative source changed; regenerate and review')
        require(lineage['fixtureSha256'] == sha(fixture), 'Family fixture changed')
        require(lineage['master'] == family + '.blend' and lineage['masterSha256'] == sha(folder / lineage['master']), 'Saved master hash mismatch')
        require(lineage['surfaceReviewMaster'] == family + '_surfaces.blend' and lineage['surfaceReviewMasterSha256'] == sha(folder / lineage['surfaceReviewMaster']), 'Packed PBR review master hash mismatch')
        expected = set(families[0]['assets'])
        require({x['id'] for x in lineage['exports']} == expected and len(lineage['exports']) == len(expected), 'Incomplete or duplicated state/LOD exports')
        for entry in lineage['exports']:
            definition_path, definition = catalog.definitions[entry['id']]
            require(entry['canonicalDefinition'] == definition_path.relative_to(catalog.root).as_posix(), 'Definition owner changed')
            require(entry['definition'] == definition, 'Source metadata changed')
            require(entry['source'] == entry['id'] + '.glb', 'Unexpected export path')
            path = folder / entry['source']
            require(entry['sha256'] == sha(path), 'Export hash mismatch')
            compare_geometry(read_geometry(catalog.source(entry['id']), catalog.root, definition),
                             read_geometry(path, folder, definition))
        print(family + ': saved master and ten LOD/state exports match current sources')


if __name__ == '__main__':
    validate()
