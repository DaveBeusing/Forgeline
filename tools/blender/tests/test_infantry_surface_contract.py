"""Production infantry skins must use the same atlas coordinates as static LODs."""
from copy import deepcopy
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import infantry_surface_contract as contract
from infantry_surface import blender_uv

SOURCE = Path(__file__).resolve().parents[3] / 'assets/source/units/directorate'


class InfantrySurfaceTests(unittest.TestCase):
    def test_production_roles_match_static_source_corners(self):
        for family in ('rifle_squad', 'combat_engineer'):
            with self.subTest(family=family):
                result = contract.validate_surface(SOURCE / f'sources/infantry/{family}_lod0.gltf',
                    SOURCE / f'authoring/infantry/{family}.glb')
                self.assertGreater(result['corners'], 10000)

    def test_reflected_atlas_coordinates_are_rejected(self):
        source = contract.corners(SOURCE / 'sources/infantry/rifle_squad_lod0.gltf')
        wrong = deepcopy(source)
        for corner in wrong:
            corner['TEXCOORD_0'] = blender_uv(corner['TEXCOORD_0'])
        with patch.object(contract, 'corners', side_effect=[source, wrong]):
            with self.assertRaisesRegex(ValueError, 'TEXCOORD_0'):
                contract.validate_surface('static', 'reflected')

    def test_geometry_shift_is_rejected(self):
        source = contract.corners(SOURCE / 'sources/infantry/rifle_squad_lod0.gltf')
        wrong = deepcopy(source)
        p = wrong[0]['POSITION']; wrong[0]['POSITION'] = (p[0] + .01, p[1], p[2])
        with patch.object(contract, 'corners', side_effect=[source, wrong]):
            with self.assertRaisesRegex(ValueError, 'POSITION'):
                contract.validate_surface('static', 'shifted')


if __name__ == '__main__':
    unittest.main()
