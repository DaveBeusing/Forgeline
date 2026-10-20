"""Command Core scope, fixed resource ceilings and numeric emissive semantics."""
import copy
import json
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, read_geometry, socket_world
from command_core_contract import ROOT, TRIANGLES, compare, refined_kit, write_emissive

REPO = Path(__file__).resolve().parents[3]


class CommandCoreContractTests(unittest.TestCase):
    def test_bootstrap_preserves_original_recipe_and_state_vocabulary(self):
        path = REPO / 'assets/source/buildings/directorate/authoring/command_core.kit.json'
        original = json.loads(path.read_text())
        before = copy.deepcopy(original)
        result = refined_kit(original)
        self.assertEqual(original, before)
        self.assertEqual(result['states'], original['states'])
        self.assertEqual(result['bounds'], original['bounds'])
        self.assertIn('protected_signal_spine', result['intactModules'])
        self.assertIn('service_piping', result['intactModules'])

    def test_emissive_mask_is_deterministic_and_confined_to_glass(self):
        with tempfile.TemporaryDirectory() as directory:
            a, b = Path(directory)/'a.png', Path(directory)/'b.png'
            write_emissive(a, 64)
            write_emissive(b, 64)
            self.assertEqual(a.read_bytes(), b.read_bytes())
            raw = a.read_bytes()
            offset, compressed = 8, b''
            while offset < len(raw):
                size = struct.unpack_from('>I', raw, offset)[0]
                if raw[offset+4:offset+8] == b'IDAT':
                    compressed += raw[offset+8:offset+8+size]
                offset += size+12
            pixels = zlib.decompress(compressed)
            lit = 0
            for y in range(64):
                for x in range(64):
                    pixel = pixels[y*257 + 1 + x*4:y*257 + 1 + x*4 + 4]
                    self.assertEqual(pixel[3], 255)
                    if any(pixel[:3]):
                        lit += 1
                        self.assertLess(x, 16)
                        self.assertGreaterEqual(y, 16)
                        self.assertLess(y, 48)
            self.assertGreater(lit, 0)

    def test_source_geometry_and_authority_remain_bounded(self):
        catalog = Catalog(REPO / 'assets/source')
        for suffix in TRIANGLES:
            aid = ROOT + suffix
            definition = catalog.definitions[aid][1]
            geometry = read_geometry(catalog.source(aid), catalog.root, definition)
            self.assertEqual(compare(aid, geometry, geometry, definition)['maximumAuthoredPositionEdit'], 0)
        primary = catalog.definitions[ROOT][1]
        self.assertEqual(primary['collisionReference'], 'building.directorate.module.collision_box')
        self.assertEqual([s['name'] for s in primary['sockets']], ['communications', 'service_access', 'roof_maintenance'])
        self.assertNotIn('mechanicalParts', primary)

    def test_geometry_cannot_change_bounds_or_topology(self):
        catalog = Catalog(REPO / 'assets/source')
        definition = catalog.definitions[ROOT][1]
        geometry = read_geometry(catalog.source(ROOT), catalog.root, definition)
        changed = copy.deepcopy(geometry)
        positions = changed['primitives'][0]['attributes']['POSITION']
        positions[0] = (3, *positions[0][1:])
        with self.assertRaises(ValueError):
            compare(ROOT, geometry, changed, definition)
        changed = copy.deepcopy(geometry)
        changed['primitives'][0]['indices'][0:3] = changed['primitives'][0]['indices'][0:1]*3
        with self.assertRaises(ValueError):
            compare(ROOT, geometry, changed, definition)

    def test_all_four_cardinal_socket_placements_preserve_height_and_radius(self):
        catalog = Catalog(REPO / 'assets/source')
        for socket in catalog.definitions[ROOT][1]['sockets']:
            radius = ((socket['x']*20)**2 + (socket['z']*20)**2)**.5
            for yaw in (0, 90, 180, 270):
                point = socket_world([socket[k] for k in 'xyz'], {'instanceScale': [20,12,20],
                    'instanceTranslation': [17,5,-29], 'instanceYawDegrees': yaw})
                self.assertAlmostEqual(point[1], 5 + socket['y']*12)
                self.assertAlmostEqual(((point[0]-17)**2 + (point[2]+29)**2)**.5, radius)


if __name__ == '__main__':
    unittest.main()
