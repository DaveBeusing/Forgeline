"""Inventory failures must remain visible rather than become acceptance."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from readiness import inventory, references, runtime_record, saved_master


class ReadinessTests(unittest.TestCase):
    def test_all_typed_links_including_skin_and_material_channels(self):
        self.assertEqual(references({'lods': [{'assetId': 'mesh.lod'}],
                                    'skinReference': 'skin.squad',
                                    'animationReferences': ['clip.walk'],
                                    'collisionReference': 'mesh.collision'},
                                   {'normalTexture': 'texture.normal'}),
                         ['clip.walk', 'mesh.collision', 'mesh.lod', 'skin.squad', 'texture.normal'])

    def test_missing_runtime_does_not_mean_compiled(self):
        self.assertEqual(runtime_record(None, None), {'status': 'not-observed'})

    def test_runtime_path_escape_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                runtime_record({'runtimePath': '../outside.flasset'}, Path(directory))

    def test_nearby_unrelated_master_does_not_prove_coverage(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'crate.blend').write_bytes(b'not a binding')
            self.assertIsNone(saved_master(root / 'crate.gltf', root))

    def test_missing_source_is_reported_and_never_approved(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = Path(directory)
            path = repo / 'assets/source/world/missing.asset.json'
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps({'id': 'mesh.world.absent', 'type': 'mesh', 'source': 'missing.gltf'}))
            result = inventory(repo)
            self.assertEqual(result['assets'][0]['sourceMaturity'], 'absent')
            self.assertIn({'id': 'mesh.world.absent', 'error': 'Missing source'}, result['errors'])
            self.assertTrue(all(value == 'pending' for value in result['acceptance'].values()))

    def test_source_escape_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = Path(directory)
            path = repo / 'assets/source/world/escape.asset.json'
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps({'id': 'mesh.world.escape', 'type': 'mesh', 'source': '../../../outside.gltf'}))
            with self.assertRaises(ValueError):
                inventory(repo)


if __name__ == '__main__':
    unittest.main()
