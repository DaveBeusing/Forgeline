import copy
import json
import hashlib
import sys
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, read_geometry, sha, geometry_stats, compare_geometry
from collections import Counter
from infrastructure_contract import canonical_geometry

ROOT=Path(__file__).resolve().parents[3]


class InfrastructureContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog=Catalog(ROOT/'assets/source')
        cls.records=json.loads((ROOT/'tools/blender/fixtures/infrastructure_sources.json').read_text())['meshes']

    def test_all_eighteen_meshes_preserve_bindings_and_only_two_reviewed_curves_change(self):
        self.assertEqual(18,len(self.records))
        for record in self.records:
            with self.subTest(asset=record['id']):
                original=self.catalog.root/record['originalSource']
                self.assertEqual('lf-normalized-json',record['originalHashMode'])
                self.assertEqual(record['originalHash'],hashlib.sha256(original.read_bytes().replace(b'\r\n',b'\n')).hexdigest())
                definition=self.catalog.definitions[record['id']][1]
                self.assertEqual({k:v for k,v in record['metadata'].items() if k!='source'},
                                 {k:v for k,v in definition.items() if k!='source'})
                before=read_geometry(original,self.catalog.root,record['metadata'])
                after=read_geometry(self.catalog.source(record['id']),self.catalog.root,definition)
                self.assertEqual(record['canonicalHash'],sha(self.catalog.source(record['id'])))
                if record.get('refinement')=='continuous-closed-ribbon':
                    self.assertIn(record['id'].rsplit('.',1)[1],('curve_short','curve_long'))
                    self.check_curve(before,after)
                elif record.get('owner')=='world-prop-masters':
                    compare_geometry(before,after)
                    self.assertIn('world/meshes/sources/blender/props/',record['canonicalSource'])
                else:self.assertEqual(before['primitives'],after['primitives'])

    def check_curve(self,before,after):
        old,new=geometry_stats(before),geometry_stats(after)
        self.assertEqual(old['boundsMin'],new['boundsMin'])
        self.assertEqual(old['boundsMax'],new['boundsMax'])
        self.assertLessEqual(new['vertices'],old['vertices'])
        self.assertLessEqual(new['triangles'],old['triangles'])
        self.assertEqual(1,len(after['primitives']))
        a,b=before['primitives'][0],after['primitives'][0]
        for key in ('mesh','primitive','name','materialSlot'):self.assertEqual(a[key],b[key])
        self.assertEqual(set(a['attributes']),set(b['attributes']))
        for axis in (0,1):
            lo=min(x[axis] for x in a['attributes']['TEXCOORD_0'])
            hi=max(x[axis] for x in a['attributes']['TEXCOORD_0'])
            for uv in b['attributes']['TEXCOORD_0']:self.assertTrue(lo<=uv[axis]<=hi)
        positions=b['attributes']['POSITION'];indices=b['indices'];edges=Counter();adjacency={}
        for i in range(0,len(indices),3):
            face=[tuple(positions[j]) for j in indices[i:i+3]]
            self.assertEqual(3,len(set(face)))
            for a,b in zip(face,face[1:]+face[:1]):
                edges[tuple(sorted((a,b)))]+=1
                adjacency.setdefault(a,set()).add(b);adjacency.setdefault(b,set()).add(a)
        self.assertTrue(all(count==2 for count in edges.values()),'Every welded edge must have two faces')
        pending=[next(iter(adjacency))];seen=set()
        while pending:
            current=pending.pop()
            if current in seen:continue
            seen.add(current);pending.extend(adjacency[current]-seen)
        self.assertEqual(len(adjacency),len(seen),'Curve must be one connected closed slab')

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
