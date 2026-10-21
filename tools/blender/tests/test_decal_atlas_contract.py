import copy
from pathlib import Path
import sys
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from decal_atlas_contract import validate_atlas,validate_carrier,validate_geometry

SOURCE=Path(__file__).resolve().parents[3]/'assets/source'


class DecalAtlasContractTests(unittest.TestCase):
    def test_all_eight_original_bakes_have_distinct_coverage_and_safe_atlas_insets(self):
        self.assertEqual(8,len(validate_atlas(SOURCE/'world/textures/decals/decal_atlas.png')['families']))

    def test_real_carrier_preserves_geometry_and_supports_the_exported_normal_basis(self):
        validate_carrier(SOURCE)

    def test_reject_changed_decal_footprint(self):
        original,candidate=validate_carrier(SOURCE);candidate=copy.deepcopy(candidate)
        row=list(candidate['primitives'][0]['attributes']['POSITION'][0]);row[0]+=.01
        candidate['primitives'][0]['attributes']['POSITION'][0]=row
        with self.assertRaises(AssertionError):validate_geometry(original,candidate)

    def test_reject_reflected_normal_tangent_basis(self):
        original,candidate=validate_carrier(SOURCE);candidate=copy.deepcopy(candidate)
        row=list(candidate['primitives'][0]['attributes']['TANGENT'][0]);row[3]*=-1
        candidate['primitives'][0]['attributes']['TANGENT'][0]=row
        with self.assertRaises(AssertionError):validate_geometry(original,candidate)


if __name__=='__main__':unittest.main()
