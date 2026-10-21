"""Check that line-ending portability does not hide authored source changes."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/blender'))
from prop_contract import PropCatalog


class PropSignatureTests(unittest.TestCase):
    def test_line_endings_are_portable_but_raw_source_hashes_remain_distinct(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            definition = json.dumps({'id': 'material.test.a', 'type': 'material', 'source': 'surface.json'}, indent=2) + '\n'
            surface = '{\n  "roughness": 0.7\n}\n'
            for ending in ('\n', '\r\n'):
                (root / 'a.asset.json').write_bytes(definition.replace('\n', ending).encode())
                (root / 'surface.json').write_bytes(surface.replace('\n', ending).encode())
                result = PropCatalog(root).signature(['material.test.a'])
                if ending == '\n':
                    first = result
                else:
                    self.assertEqual(first[0], result[0])
                    self.assertNotEqual(first[1], result[1])
            (root / 'surface.json').write_text(surface.replace('0.7', '0.8'), encoding='utf-8')
            self.assertNotEqual(first[0], PropCatalog(root).signature(['material.test.a'])[0])

    def test_frozen_batch_rejects_definition_drift_and_does_not_cache_output_clones(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            a, b = root / 'canonical', root / 'clone'
            a.mkdir(); b.mkdir()
            definition = {'id': 'material.test.a', 'type': 'material', 'source': 'surface.json'}
            for path in (a, b):
                (path / 'a.asset.json').write_text(json.dumps(definition), encoding='utf-8')
                (path / 'surface.json').write_text('{"roughness":0.7}', encoding='utf-8')
            try:
                PropCatalog.freeze(a)
                before = PropCatalog(a).signature(['material.test.a'])
                (b / 'surface.json').write_text('{"roughness":0.8}', encoding='utf-8')
                self.assertNotEqual(before[0], PropCatalog(b).signature(['material.test.a'])[0])
                definition['textureReferences'] = ['texture.test.changed']
                (a / 'a.asset.json').write_text(json.dumps(definition), encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'Canonical definition changed'):
                    PropCatalog(a).signature(['material.test.a'])
            finally:
                PropCatalog._frozen = None


class PipeAxisTests(unittest.TestCase):
    def test_baked_pipe_axis_retains_uv_topology_and_handedness(self):
        from pipe_contract import align_straight_pipe
        source = {'primitives': [{'attributes': {'POSITION': [(0, 0, .5)],
            'NORMAL': [(0, 0, 1)], 'TANGENT': [(1, 0, 0, -1)],
            'TEXCOORD_0': [(.125, .25)]}, 'indices': [0, 0, 0], 'materialSlot': 0}]}
        result = align_straight_pipe(source)['primitives'][0]
        self.assertEqual((.5, 0, 0), result['attributes']['POSITION'][0])
        self.assertEqual((1, 0, 0), result['attributes']['NORMAL'][0])
        self.assertEqual((0, 0, -1, -1), result['attributes']['TANGENT'][0])
        self.assertEqual(source['primitives'][0]['attributes']['TEXCOORD_0'], result['attributes']['TEXCOORD_0'])
        self.assertEqual(source['primitives'][0]['indices'], result['indices'])
        self.assertEqual((0, 0, .5), source['primitives'][0]['attributes']['POSITION'][0])


if __name__ == '__main__':
    unittest.main()
