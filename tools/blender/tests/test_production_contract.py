import copy
import math
import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from production_contract import gate, measure, texture_cost, validate_surface


def quad():
    return {'primitives':[{'name':'chart','materialSlot':0,'indices':[0,1,2,0,2,3],'attributes':{'POSITION':[(0,0,0),(1,0,0),(1,1,0),(0,1,0)],'NORMAL':[(0,0,1)]*4,'TEXCOORD_0':[(.1,.1),(.2,.1),(.2,.2),(.1,.2)]}}]}


class SurfaceContracts(unittest.TestCase):
    def setUp(self):
        self.g=quad();self.limits={'maxTriangles':2,'maxSections':1,'maxGeometryBytes':528,'minSilhouetteIou':.99,'maxRelativeBoundsError':1e-5}
    def test_identical_silhouette(self):self.assertEqual(1,gate(self.g,self.g,self.limits)['minimumSilhouetteIou'])
    def test_silhouette_detects_lost_role_form(self):
        changed=copy.deepcopy(self.g);changed['primitives'][0]['indices']=[0,1,2]
        with self.assertRaisesRegex(ValueError,'silhouette'):gate(self.g,changed,self.limits)
    def test_budget_is_required_and_not_universal(self):
        with self.assertRaisesRegex(ValueError,'Explicit'):gate(self.g,self.g,{})
    def test_nonfinite_limit_rejected(self):
        self.limits['maxTriangles']=math.inf
        with self.assertRaisesRegex(ValueError,'Nonfinite'):gate(self.g,self.g,self.limits)
    def test_section_budget(self):
        self.limits['maxSections']=0
        with self.assertRaises(ValueError):gate(self.g,self.g,self.limits)
    def test_triangle_budget(self):
        self.limits['maxTriangles']=1
        with self.assertRaisesRegex(ValueError,'Triangle budget'):gate(self.g,self.g,self.limits)
    def test_protected_bounds(self):
        changed=copy.deepcopy(self.g);changed['primitives'][0]['attributes']['POSITION'][1]=(1.01,0,0)
        with self.assertRaisesRegex(ValueError,'bounds'):gate(self.g,changed,{**self.limits,'minSilhouetteIou':0})
    def test_material_slot(self):
        changed=copy.deepcopy(self.g);changed['primitives'][0]['materialSlot']=1
        with self.assertRaisesRegex(ValueError,'material'):gate(self.g,changed,self.limits)
    def test_atlas_extent(self):
        changed=copy.deepcopy(self.g);changed['primitives'][0]['attributes']['TEXCOORD_0'][0]=(.09,.1)
        with self.assertRaisesRegex(ValueError,'Atlas'):gate(self.g,changed,self.limits)
    def test_vertex_expansion_memory_budget(self):
        self.limits['maxGeometryBytes']=100
        with self.assertRaisesRegex(ValueError,'memory budget'):gate(self.g,self.g,self.limits)
    def test_disconnected_atlas_chart_gap(self):
        original=quad();p=original['primitives'][0]
        p['attributes']['POSITION']*=2;p['attributes']['NORMAL']*=2
        p['attributes']['TEXCOORD_0'] += [(u+.5,v) for u,v in p['attributes']['TEXCOORD_0']]
        p['indices'] += [i+4 for i in p['indices']]
        changed=copy.deepcopy(original);changed['primitives'][0]['attributes']['TEXCOORD_0'][0]=(.4,.1)
        with self.assertRaisesRegex(ValueError,'chart'):gate(original,changed,{**self.limits,'maxTriangles':4})
    def test_invalid_normal(self):
        self.g['primitives'][0]['attributes']['NORMAL'][0]=(0,0,0)
        with self.assertRaisesRegex(ValueError,'normal'):validate_surface(self.g)
    def test_uv_degeneracy(self):
        self.g['primitives'][0]['attributes']['TEXCOORD_0']=[(0,0)]*4
        with self.assertRaisesRegex(ValueError,'UV'):validate_surface(self.g)
    def test_tangent_handedness(self):
        self.g['primitives'][0]['attributes']['TANGENT']=[(1,0,0,0)]*4
        with self.assertRaisesRegex(ValueError,'tangent'):validate_surface(self.g)
    def test_nonfinite_channel(self):
        self.g['primitives'][0]['attributes']['POSITION'][0]=(math.nan,0,0)
        with self.assertRaisesRegex(ValueError,'Nonfinite'):validate_surface(self.g)
    def test_bc7_mips_include_minimum_block(self):self.assertEqual(48,texture_cost(4,4,'BC7',3))
    def test_texture_cost_formats(self):
        self.assertEqual(64,texture_cost(4,4,'Rgba8',1))
        with self.assertRaises(ValueError):texture_cost(4,4,'unknown',1)

if __name__=='__main__':unittest.main()
