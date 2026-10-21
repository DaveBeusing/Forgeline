"""Measure family-scoped surface inspection using unchanged production limits."""
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, fixture_selection, read_geometry, write_json
from production_contract import geometry_stats

def prepare(output):
    catalog = Catalog(ROOT / 'assets/source')
    for family in ('vehicle_factory', 'power_plant', 'barracks', 'radar'):
        ids, _ = fixture_selection(catalog, Path(__file__).with_name(family + '.json'), [])
        jobs = {}
        for aid in ids:
            definition = catalog.definitions[aid][1]
            if definition['type'] != 'mesh' or not aid.startswith('building.directorate.' + family):
                continue
            geometry = read_geometry(catalog.source(aid), catalog.root, definition)
            stats = geometry_stats(geometry)
            jobs[aid] = {'ratio': 1, 'bevelMetres': 0, 'weightedNormals': False, 'protectedParts': [],
                'limits': {'maxTriangles': stats['triangles'], 'maxSections': len(geometry['primitives']),
                'maxGeometryBytes': sum(len(p['attributes']['POSITION']) * 48 + len(p['indices']) * 4 for p in geometry['primitives']),
                'minSilhouetteIou': .99, 'maxRelativeBoundsError': .00001}}
        if len(jobs) != 10:
            raise ValueError('Expected ten LOD/state meshes for ' + family)
        write_json(output / (family + '.json'), {'schemaVersion': 1, 'maxTexturePayloadBytes': 7325040, 'jobs': jobs})

if __name__ == '__main__':
    prepare(Path(sys.argv[1]))
