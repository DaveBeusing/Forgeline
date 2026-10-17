"""Regression checks over the production family, independent of Blender availability."""
import copy,json,sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog,read_geometry,sha
from tank_contract import ROOT,LIMITS,compare
REPO=Path(__file__).resolve().parents[3]

class TankContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog=Catalog(REPO/'assets/source')
        cls.baseline=json.loads((REPO/'docs/evidence/directorate-tank/baseline.json').read_text())['assets']
    def geometry(self,suffix='.damaged'):
        aid=ROOT+suffix;d=self.catalog.definitions[aid][1]
        return aid,d,read_geometry(self.catalog.source(aid),self.catalog.root,d)
    def test_stable_gameplay_and_binding_identity(self):
        for row in self.baseline:
            aid=row['id'];p,d=self.catalog.definitions[aid];before=row['definition']
            for key in set(before)|set(d):
                if key in ('source','mechanicalParts'):continue
                self.assertEqual(before.get(key),d.get(key),(aid,key))
            a=before.get('mechanicalParts',[]);b=d.get('mechanicalParts',[])
            self.assertEqual([{k:v for k,v in x.items() if k not in ('firstIndex','indexCount')} for x in a],[{k:v for k,v in x.items() if k not in ('firstIndex','indexCount')} for x in b])
            if aid.endswith('.collision'):self.assertEqual(row['sourceSha256'],sha(self.catalog.source(aid)))
    def test_all_current_geometry_meets_existing_budgets(self):
        for suffix,limit in LIMITS.items():
            aid,d,g=self.geometry(suffix);compare(aid,g,g,d)
            self.assertLessEqual(sum(len(p['indices'])//3 for p in g['primitives']),limit)
    def test_rest_shape_edit_is_supported(self):
        aid,d,g=self.geometry();edited=copy.deepcopy(g)
        for primitive in edited['primitives']:
            primitive['attributes']['POSITION']=[(v[0]+.001,v[1],v[2]) for v in primitive['attributes']['POSITION']]
        self.assertGreater(compare(aid,g,edited,d)['maximumAuthoredPositionEdit'],0)
    def test_silhouette_escape_rejected(self):
        aid,d,g=self.geometry();edited=copy.deepcopy(g);edited['primitives'][0]['attributes']['POSITION'][0]=(2,0,0)
        with self.assertRaisesRegex(ValueError,'silhouette'):compare(aid,g,edited,d)
    def test_nonfinite_edit_rejected(self):
        aid,d,g=self.geometry();edited=copy.deepcopy(g);edited['primitives'][0]['attributes']['POSITION'][0]=(float('nan'),0,0)
        with self.assertRaisesRegex(ValueError,'Non-finite'):compare(aid,g,edited,d)
    def test_moving_weapon_edit_rejected(self):
        aid,d,g=self.geometry('.turret');edited=copy.deepcopy(g);part=d['mechanicalParts'][1];primitive=edited['primitives'][0]
        index=primitive['indices'][part['firstIndex']];v=primitive['attributes']['POSITION'][index];primitive['attributes']['POSITION'][index]=(v[0]+.001,v[1],v[2])
        with self.assertRaisesRegex(ValueError,'Moving part'):compare(aid,g,edited,d)
    def test_collision_edit_rejected(self):
        aid,d,g=self.geometry('.collision');edited=copy.deepcopy(g);v=edited['primitives'][0]['attributes']['POSITION'][0];edited['primitives'][0]['attributes']['POSITION'][0]=(v[0]+.001,v[1],v[2])
        with self.assertRaises(ValueError):compare(aid,g,edited,d)
    def test_uv_edit_rejected(self):
        aid,d,g=self.geometry();edited=copy.deepcopy(g);edited['primitives'][0]['attributes']['TEXCOORD_0'][0]=(0,0)
        with self.assertRaises(ValueError):compare(aid,g,edited,d)
    def test_explicit_normal_edit_rejected(self):
        aid,d,g=self.geometry();edited=copy.deepcopy(g);normal=edited['primitives'][0]['attributes']['NORMAL'][0];edited['primitives'][0]['attributes']['NORMAL'][0]=[-v for v in normal]
        with self.assertRaisesRegex(ValueError,'explicit normal'):compare(aid,g,edited,d)
    def test_degenerate_edit_rejected(self):
        aid,d,g=self.geometry();edited=copy.deepcopy(g);p=edited['primitives'][0];a,b,c=p['indices'][:3];p['attributes']['POSITION'][b]=p['attributes']['POSITION'][a]
        with self.assertRaisesRegex(ValueError,'Degenerate'):compare(aid,g,edited,d)

if __name__=='__main__':unittest.main()
