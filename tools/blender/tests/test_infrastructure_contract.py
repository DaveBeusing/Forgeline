import copy
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, read_geometry, sha
from infrastructure_contract import canonical_geometry

ROOT=Path(__file__).resolve().parents[3]


class InfrastructureContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog=Catalog(ROOT/'assets/source')
        cls.records=json.loads((ROOT/'tools/blender/fixtures/infrastructure_sources.json').read_text())['meshes']

    def test_all_eighteen_canonical_meshes_preserve_original_channels_and_metadata(self):
        self.assertEqual(18,len(self.records))
        for record in self.records:
            with self.subTest(asset=record['id']):
                original=self.catalog.root/record['originalSource']
                self.assertEqual(record['originalHash'],sha(original))
                definition=self.catalog.definitions[record['id']][1]
                self.assertEqual({k:v for k,v in record['metadata'].items() if k!='source'},
                                 {k:v for k,v in definition.items() if k!='source'})
                before=read_geometry(original,self.catalog.root,record['metadata'])
                after=read_geometry(self.catalog.source(record['id']),self.catalog.root,definition)
                self.assertEqual(before['primitives'],after['primitives'])

    def geometry(self):
        aid='infrastructure.directorate.bridge.road.intact'
        return read_geometry(self.catalog.source(aid),self.catalog.root,self.catalog.definitions[aid][1])

    def test_bounded_interior_vertex_edit_is_preserved(self):
        before=self.geometry();after=copy.deepcopy(before)
        p=list(after['primitives'][0]['attributes']['POSITION'][0]);p[0]+=.001
        after['primitives'][0]['attributes']['POSITION'][0]=tuple(p)
        actual,delta=canonical_geometry(before,after)
        self.assertGreater(delta,.0009)
        self.assertEqual(after['primitives'],actual['primitives'])

    def test_collision_edit_rejects(self):
        before=self.geometry();after=copy.deepcopy(before)
        p=list(after['primitives'][0]['attributes']['POSITION'][0]);p[0]+=.001
        after['primitives'][0]['attributes']['POSITION'][0]=tuple(p)
        with self.assertRaises(ValueError):canonical_geometry(before,after,True)

    def test_outside_bounds_and_large_edits_reject(self):
        before=self.geometry()
        for delta in (-.06,.02,float('nan')):
            after=copy.deepcopy(before);p=list(after['primitives'][0]['attributes']['POSITION'][0]);p[0]+=delta
            after['primitives'][0]['attributes']['POSITION'][0]=tuple(p)
            with self.assertRaises(ValueError):canonical_geometry(before,after)

    def test_surface_and_topology_edits_reject(self):
        before=self.geometry()
        for channel in ('NORMAL','TEXCOORD_0'):
            after=copy.deepcopy(before);p=list(after['primitives'][0]['attributes'][channel][0]);p[0]+=.1
            after['primitives'][0]['attributes'][channel][0]=tuple(p)
            with self.assertRaises(ValueError):canonical_geometry(before,after)
        after=copy.deepcopy(before);after['primitives'][0]['indices'][0:3]=list(reversed(after['primitives'][0]['indices'][0:3]))
        with self.assertRaises(ValueError):canonical_geometry(before,after)


if __name__=='__main__':unittest.main()
