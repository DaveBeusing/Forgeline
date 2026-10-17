import copy,sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from motion_contract import validate_motion,edited_definition

def fixture():
    return {'sockets':[{'name':'motion_wheel','x':.2,'y':0,'z':0}], 'mechanicalParts':[{'name':'wheel','firstIndex':0,'indexCount':6,'parent':-1,'motion':'wheel','socket':'motion_wheel','rate':1.5,'travel':0}]}
def edits():return {'wheel':{'motion':'wheel','rate':1.5,'travel':0,'pivot':[.2,0,0]}}

class MotionContractTests(unittest.TestCase):
    def test_default_keeps_metadata_exact(self):
        d=fixture();self.assertEqual(d,edited_definition(d,edits(),.025,6))
    def test_explicit_rate_and_pivot_edit_keeps_source_immutable(self):
        d=fixture();e=edits();e['wheel']['rate']=-2;e['wheel']['pivot'][0]=.21
        u=edited_definition(d,e,.025,6);self.assertEqual(-2,u['mechanicalParts'][0]['rate']);self.assertEqual(.2,d['sockets'][0]['x']);self.assertEqual(.21,u['sockets'][0]['x'])
    def test_float_property_noise_keeps_canonical_socket(self):
        e=edits();e['wheel']['pivot'][0]+=.00000001
        self.assertEqual(fixture(),edited_definition(fixture(),e,.025,6))
    def test_reject_unknown_motion(self):
        d=fixture();d['mechanicalParts'][0]['motion']='skeletal'
        with self.assertRaises(ValueError):validate_motion(d,6)
    def test_reject_nonfinite_rate(self):
        d=fixture();d['mechanicalParts'][0]['rate']=float('nan')
        with self.assertRaises(ValueError):validate_motion(d,6)
    def test_reject_unbounded_travel(self):
        d=fixture();d['mechanicalParts'][0]['travel']=2.01
        with self.assertRaises(ValueError):validate_motion(d,6)
    def test_reject_missing_socket(self):
        d=fixture();d['sockets']=[]
        with self.assertRaises(ValueError):validate_motion(d,6)
    def test_reject_invalid_partition(self):
        for key,value in [('firstIndex',3),('indexCount',4),('parent',0)]:
            d=fixture();d['mechanicalParts'][0][key]=value
            with self.assertRaises(ValueError):validate_motion(d,6)
    def test_reject_missing_part(self):
        with self.assertRaises(ValueError):edited_definition(fixture(),{},.025,6)
    def test_reject_large_pivot_move(self):
        e=edits();e['wheel']['pivot'][0]=.3
        with self.assertRaises(ValueError):edited_definition(fixture(),e,.025,6)
    def test_reject_gameplay_socket_edit(self):
        d=fixture();d['sockets'][0]['name']='weapon_muzzle';d['mechanicalParts'][0]['socket']='weapon_muzzle';e=edits();e['wheel']['pivot'][0]=.21
        with self.assertRaises(ValueError):edited_definition(d,e,.025,6)
    def test_reject_gameplay_coupled_cannon_pivot(self):
        d=fixture();d['mechanicalParts'][0]['motion']='cannon';e=edits();e['wheel']['motion']='cannon';e['wheel']['pivot'][0]=.21
        with self.assertRaises(ValueError):edited_definition(d,e,.025,6)
    def test_shared_socket_requires_consistent_edit_in_both_orders(self):
        for reverse in (False,True):
            d=fixture();d['mechanicalParts'].append({**d['mechanicalParts'][0],'name':'other','firstIndex':6})
            e=edits();e['other']=copy.deepcopy(e['wheel']);e['wheel']['pivot'][0]=.21
            if reverse:d['mechanicalParts'].reverse();d['mechanicalParts'][0]['firstIndex']=0;d['mechanicalParts'][1]['firstIndex']=6
            with self.assertRaises(ValueError):edited_definition(d,e,.025,12)
    def test_reject_unsupported_properties(self):
        e=edits();e['wheel']['clip']='walk'
        with self.assertRaises(ValueError):edited_definition(fixture(),e,.025,6)
    def test_missing_metadata_is_static(self):validate_motion({},36)
    def test_reject_part_cap(self):
        d=fixture();d['mechanicalParts']=[{**d['mechanicalParts'][0],'name':str(i),'firstIndex':i*6} for i in range(33)]
        with self.assertRaises(ValueError):validate_motion(d,198)
if __name__=='__main__':unittest.main()
