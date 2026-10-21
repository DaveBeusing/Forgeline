"""Logistics edits preserve real geometry bounds and compiler channels."""
import copy
import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from logistics_contract import compare


class LogisticsContractTests(unittest.TestCase):
    def setUp(self):
        self.geometry = {'primitives': [{'mesh': 0, 'primitive': 0, 'name': 'panel',
            'materialSlot': 0, 'indices': [0, 1, 2], 'attributes': {
                'POSITION': [[0,0,0], [.2,0,0], [0,.2,0]],
                'NORMAL': [[0,0,1]]*3, 'TEXCOORD_0': [[0,0],[1,0],[0,1]]}}]}

    def check(self, candidate):
        return compare(self.geometry, candidate, [-.5]*3, [.5]*3)

    def test_saved_planar_edit_is_observed(self):
        edited = copy.deepcopy(self.geometry)
        for p in edited['primitives'][0]['attributes']['POSITION']: p[0] += .01
        self.assertAlmostEqual(.01, self.check(edited)['maximumAuthoredPositionEdit'])

    def test_edit_cannot_escape_retained_envelope(self):
        edited = copy.deepcopy(self.geometry)
        edited['primitives'][0]['attributes']['POSITION'][0][0] = -1
        with self.assertRaisesRegex(ValueError, 'retained bounds'): self.check(edited)

    def test_nonplanar_edit_requires_explicit_normal_authoring(self):
        edited = copy.deepcopy(self.geometry)
        edited['primitives'][0]['attributes']['POSITION'][0][2] = .1
        with self.assertRaisesRegex(ValueError, 'authored normals'): self.check(edited)

    def test_degenerate_face_is_rejected(self):
        edited = copy.deepcopy(self.geometry)
        edited['primitives'][0]['attributes']['POSITION'][1] = [0,0,0]
        with self.assertRaisesRegex(ValueError, 'Degenerate'): self.check(edited)

    def test_uv_edit_is_rejected(self):
        edited = copy.deepcopy(self.geometry)
        edited['primitives'][0]['attributes']['TEXCOORD_0'][0][0] = .1
        with self.assertRaisesRegex(ValueError, 'TEXCOORD_0'): self.check(edited)

    def test_material_assignment_is_rejected(self):
        edited = copy.deepcopy(self.geometry)
        edited['primitives'][0]['materialSlot'] = 1
        with self.assertRaisesRegex(ValueError, 'topology'): self.check(edited)


if __name__ == '__main__': unittest.main()
