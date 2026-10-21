import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, read_geometry
from logistics_truck_contract import FAMILIES, SUFFIXES, compare, component_parts


class LogisticsTruckContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog = Catalog(Path(__file__).resolve().parents[3] / 'assets/source')

    def geometry(self, family='cargo_truck', suffix=''):
        aid = 'unit.directorate.' + family + suffix
        definition = self.catalog.definitions[aid][1]
        return aid, definition, read_geometry(self.catalog.source(aid), self.catalog.root, definition)

    def test_every_existing_state_and_lod_preserves_geometry(self):
        for family in FAMILIES:
            for suffix in SUFFIXES:
                with self.subTest(family=family, suffix=suffix):
                    aid, definition, geometry = self.geometry(family, suffix)
                    compare(aid, geometry, copy.deepcopy(geometry), definition)

    def test_components_cover_each_index_and_retain_wheel_anchors(self):
        for family in FAMILIES:
            for suffix in SUFFIXES[:-1]:
                aid, definition, geometry = self.geometry(family, suffix)
                parts = component_parts(definition, geometry)
                moving = [p for p in parts if p['motion'] != 'static']
                self.assertEqual(len(moving), 8 if not suffix else 0)
                for part in moving:
                    original = next(p for p in definition['mechanicalParts'] if p['name'] == part['name'])
                    self.assertEqual({k:v for k,v in part.items() if k != 'parent'},
                                     {k:v for k,v in original.items() if k != 'parent'})

    def test_component_gap_rejected(self):
        _, definition, geometry = self.geometry()
        geometry['document']['extras']['componentHierarchy'][1]['firstIndex'] += 3
        with self.assertRaisesRegex(ValueError, 'partition'):
            component_parts(definition, geometry)

    def test_wheel_edit_rejected(self):
        aid, definition, before = self.geometry()
        after = copy.deepcopy(before)
        primitive = after['primitives'][0]
        wheel = next(p for p in definition['mechanicalParts'] if p['motion'] == 'wheel')
        index = primitive['indices'][wheel['firstIndex']]
        point = list(primitive['attributes']['POSITION'][index])
        point[1] += .0001
        primitive['attributes']['POSITION'][index] = point
        with self.assertRaisesRegex(ValueError, 'mechanical anchor'):
            compare(aid, before, after, definition)

    def test_silhouette_growth_rejected(self):
        aid, definition, before = self.geometry()
        after = copy.deepcopy(before)
        points = after['primitives'][0]['attributes']['POSITION']
        i = max(range(len(points)), key=lambda index: points[index][0])
        points[i] = (points[i][0]+.001, *points[i][1:])
        with self.assertRaisesRegex(ValueError, 'envelope'):
            compare(aid, before, after, definition)

    def test_uv_edit_rejected(self):
        aid, definition, before = self.geometry()
        after = copy.deepcopy(before)
        after['primitives'][0]['attributes']['TEXCOORD_0'][0] = (0, 0)
        with self.assertRaisesRegex(ValueError, 'TEXCOORD_0'):
            compare(aid, before, after, definition)


if __name__ == '__main__':
    unittest.main()
