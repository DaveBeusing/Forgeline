"""Retained exact source channels and metadata for the reviewed tank finish."""
import hashlib
import json
import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, read_geometry

REPO = Path(__file__).resolve().parents[3]


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def inventory(catalog, aid):
    definition = catalog.definitions[aid][1]
    geometry = read_geometry(catalog.source(aid), catalog.root, definition)
    return {
        'definition': digest({k: v for k, v in definition.items() if k != 'source'}),
        'primitives': [{
            'identity': digest({k: p[k] for k in ('mesh', 'primitive', 'name', 'materialSlot', 'indices')}),
            'channels': {k: digest(v) for k, v in p['attributes'].items()}
        } for p in geometry['primitives']]
    }


class TankFinishSourceTests(unittest.TestCase):
    def test_exact_eight_mesh_sources_and_gameplay_metadata(self):
        expected = json.loads((REPO / 'tools/blender/fixtures/tank_finish_sources.json').read_text())
        catalog = Catalog(REPO / 'assets/source')
        self.assertEqual(8, len(expected['assets']))
        for aid, contract in expected['assets'].items():
            self.assertEqual(contract, inventory(catalog, aid), aid)


if __name__ == '__main__':
    unittest.main()
