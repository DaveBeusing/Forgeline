"""Small fixtures exercise audit detection without changing production sources."""
import base64
import json
from pathlib import Path
import struct
import tempfile
import unittest

from PIL import Image
import source_audit as audit


def triangle(reverse=False):
    arrays = [([(0, 0, 0), (1, 0, 0), (0, 1, 0)], 'VEC3'),
              ([(0, 0, 1)] * 3, 'VEC3'), ([(0, 0), (1, 0), (0, 1)], 'VEC2')]
    data, accessors, views = bytearray(), [], []
    for rows, kind in arrays:
        offset = len(data)
        for row in rows:
            data.extend(struct.pack('<' + 'f' * len(row), *row))
        views.append({'buffer': 0, 'byteOffset': offset, 'byteLength': len(data) - offset})
        accessors.append({'bufferView': len(views) - 1, 'componentType': 5126, 'count': 3, 'type': kind})
    offset = len(data)
    data.extend(struct.pack('<III', *(0, 2, 1) if reverse else (0, 1, 2)))
    views.append({'buffer': 0, 'byteOffset': offset, 'byteLength': 12})
    accessors.append({'bufferView': 3, 'componentType': 5125, 'count': 3, 'type': 'SCALAR'})
    document = {'asset': {'version': '2.0'}, 'buffers': [{'byteLength': len(data), 'uri':
                'data:application/octet-stream;base64,' + base64.b64encode(data).decode()}],
                'bufferViews': views, 'accessors': accessors,
                'meshes': [{'primitives': [{'attributes': {'POSITION': 0, 'NORMAL': 1, 'TEXCOORD_0': 2}, 'indices': 3}]}]}
    return document, [bytes(data)]


class AuditTests(unittest.TestCase):
    def test_valid_triangle(self):
        stats, issues = audit.mesh_stats(*triangle())
        self.assertEqual(1, stats['triangles'])
        self.assertEqual([], issues)

    def test_winding_is_review_warning_not_automatic_repair(self):
        stats, issues = audit.mesh_stats(*triangle(True))
        self.assertEqual(1, stats['opposedNormalTriangles'])
        self.assertEqual('normal-winding-disagreement', issues[0][0])

    def test_large_truncated_accessor_rejected_before_allocation(self):
        document, buffers = triangle()
        document['accessors'][0]['count'] = 30_000_000
        with self.assertRaisesRegex(ValueError, 'exceeds buffer view'):
            audit.accessor(document, buffers, 0)

    def test_duplicate_json_property_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            audit.read_json(b'{"id":"a", "id":"b"}')

    def test_nonfinite_json_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'nonfinite'):
            audit.read_json(b'{"x":NaN}')

    def test_outside_source_path_is_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            p = Path(root)
            with self.assertRaisesRegex(ValueError, 'escapes'):
                audit.contained(p, p, '../not-an-asset')

    def test_valid_catalog_and_corrupt_texture(self):
        with tempfile.TemporaryDirectory() as root:
            p = Path(root)
            document, _ = triangle()
            (p / 'model.gltf').write_text(json.dumps(document))
            (p / 'model.asset.json').write_text(json.dumps({'id': 'mesh.fixture', 'type': 'mesh', 'source': 'model.gltf'}))
            Image.new('RGB', (4, 4), (128, 128, 255)).save(p / 'normal.png')
            (p / 'normal.asset.json').write_text(json.dumps({'id': 'texture.normal', 'type': 'texture', 'source': 'normal.png', 'textureUsage': 'normal', 'textureColorSpace': 'linear'}))
            result = audit.audit(p)
            self.assertEqual(0, result['summary']['errors'])
            self.assertEqual(1, result['summary']['mesh'])
            self.assertEqual(1, result['summary']['texture'])
            (p / 'broken.png').write_bytes(b'broken')
            result = audit.audit(p)
            self.assertEqual(1, result['summary']['errors'])

    def test_wrong_normal_colorspace_is_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            p = Path(root)
            Image.new('RGB', (1, 1), (128, 128, 255)).save(p / 'normal.png')
            (p / 'normal.asset.json').write_text(json.dumps({'id': 'texture.normal', 'type': 'texture', 'source': 'normal.png', 'textureUsage': 'normal'}))
            self.assertEqual(1, audit.audit(p)['summary']['errors'])

    def test_missing_material_dependency_is_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            p = Path(root)
            (p / 'material.json').write_text(json.dumps({'normalTexture': 'texture.missing'}))
            (p / 'material.asset.json').write_text(json.dumps({'id': 'material.fixture', 'type': 'material', 'source': 'material.json'}))
            self.assertEqual(1, audit.audit(p)['summary']['errors'])

    def test_glb_length_is_validated(self):
        with tempfile.TemporaryDirectory() as root:
            p = Path(root)
            (p / 'model.glb').write_bytes(struct.pack('<IIIII', 0x46546c67, 2, 100, 0, 0x4e4f534a))
            with self.assertRaisesRegex(ValueError, 'magic/version/length'):
                audit.load_mesh(p / 'model.glb', p)


if __name__ == '__main__':
    unittest.main()
