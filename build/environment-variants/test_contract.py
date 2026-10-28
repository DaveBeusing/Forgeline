import importlib.util
import json
import math
import tempfile
import unittest
from pathlib import Path

SPEC=importlib.util.spec_from_file_location('variant_author',Path(__file__).with_name('author.py'))
author=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(author)


class VariantContractTests(unittest.TestCase):
    def test_exports_are_deterministic_and_preserve_topology_atlas_and_ground(self):
        source=author.ROOT/'assets/source'
        definitions={}
        for path in source.rglob('*.asset.json'):
            definition=json.loads(path.read_text())
            self.assertNotIn(definition['id'],definitions)
            definitions[definition['id']]=(path,definition)
        with tempfile.TemporaryDirectory() as first,tempfile.TemporaryDirectory() as second:
            reports=author.run(source,Path(first))
            self.assertEqual(reports,author.run(source,Path(second)))
            self.assertEqual(6,len(reports))
            for row in reports:
                original_path,definition=definitions[row['sourceId']]
                original_source=original_path.parent/definition['source']
                self.assertEqual(row['inputSha256'],author.sha(original_source))
                before=author.read_geometry(original_source,source,definition)['primitives'][0]
                name=row['id'].split('.')[-1]
                target=Path(first)/'world/meshes/variants'/f'{name}.glb'
                self.assertEqual(target.read_bytes(),(Path(second)/target.relative_to(first)).read_bytes())
                after=author.read_geometry(target,Path(first),definition)['primitives'][0]
                self.assertEqual(before['indices'],after['indices'])
                self.assertEqual(before['attributes']['TEXCOORD_0'],after['attributes']['TEXCOORD_0'])
                self.assertNotEqual(before['attributes']['POSITION'],after['attributes']['POSITION'])
                positions=after['attributes']['POSITION']
                old=before['attributes']['POSITION']
                self.assertEqual(min(v[1] for v in old),min(v[1] for v in positions))
                self.assertEqual(max(v[1] for v in old),max(v[1] for v in positions))
                self.assertTrue(all(abs(v[0])<=.500001 and abs(v[2])<=.500001 for v in positions))
                self.assertTrue(all(abs(math.sqrt(sum(x*x for x in n))-1)<1e-5 for n in after['attributes']['NORMAL']))


if __name__=='__main__':unittest.main()
