"""The explicit finish migration cannot change mechanics or other surfaces."""
import copy
import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from tank_surface_contract import ROOT, refine, compare


class TankSurfaceContractTests(unittest.TestCase):
    def geometry(self):
        return {'primitives': [{'mesh': 0, 'primitive': 0, 'name': 'armor', 'materialSlot': 0, 'indices': [0, 1, 2],
                'attributes': {'POSITION': [[0, 0, 0], [1, 0, 0], [0, 1, 0]],
                               'NORMAL': [[0, 0, 1]] * 3,
                               'TEXCOORD_0': [[.018, .518], [.232, .518], [.018, .732]]}}]}

    def test_only_chart_translation_changes(self):
        before = self.geometry()
        after, count = refine(ROOT, before, {})
        self.assertEqual(3, count)
        compare(ROOT, before, after, {})
        self.assertEqual(before, self.geometry())
        for a, b in zip(before['primitives'][0]['attributes']['TEXCOORD_0'],
                        after['primitives'][0]['attributes']['TEXCOORD_0']):
            self.assertEqual(a[0], b[0])
            self.assertEqual(a[1] - .5, b[1])
        self.assertEqual((after, 0), refine(ROOT, after, {}))

    def test_damage_collision_and_lower_lod_are_unchanged(self):
        for suffix in ('.collision', '.damaged', '.critical', '.wreck', '.lod2'):
            before = self.geometry()
            self.assertEqual((before, 0), refine(ROOT + suffix, before, {}))

    def test_moving_part_refuses_repaint(self):
        definition = {'mechanicalParts': [{'motion': 'wheel', 'firstIndex': 0, 'indexCount': 3}]}
        with self.assertRaisesRegex(ValueError, 'moving geometry'):
            refine(ROOT, self.geometry(), definition)

    def test_extra_geometry_and_uv_edits_reject(self):
        before = self.geometry()
        after, _ = refine(ROOT, before, {})
        for channel in ('POSITION', 'NORMAL', 'TEXCOORD_0'):
            invalid = copy.deepcopy(after)
            invalid['primitives'][0]['attributes'][channel][0][0] += .01
            with self.assertRaises(ValueError):
                compare(ROOT, before, invalid, {})


if __name__ == '__main__':
    unittest.main()
