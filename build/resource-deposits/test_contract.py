"""Check that line-ending portability does not hide authored source changes."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/blender'))
from mineral_contract import MineralCatalog


class MineralSignatureTests(unittest.TestCase):
    def test_line_endings_are_portable_but_raw_source_hashes_remain_distinct(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            definition = json.dumps({'id': 'material.test.a', 'type': 'material', 'source': 'surface.json'}, indent=2) + '\n'
            surface = '{\n  "roughness": 0.7\n}\n'
            for ending in ('\n', '\r\n'):
                (root / 'a.asset.json').write_bytes(definition.replace('\n', ending).encode())
                (root / 'surface.json').write_bytes(surface.replace('\n', ending).encode())
                result = MineralCatalog(root).signature(['material.test.a'])
                if ending == '\n':
                    first = result
                else:
                    self.assertEqual(first[0], result[0])
                    self.assertNotEqual(first[1], result[1])
            (root / 'surface.json').write_text(surface.replace('0.7', '0.8'), encoding='utf-8')
            self.assertNotEqual(first[0], MineralCatalog(root).signature(['material.test.a'])[0])


if __name__ == '__main__':
    unittest.main()
