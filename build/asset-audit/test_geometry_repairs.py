"""Regression fixtures for source-frame continuity and crossed track sidewalls."""
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from source_geometry import pipe_frames, inset_profile
import source_audit as audit
from catalog_contracts import animation_stats


class GeometryRepairTests(unittest.TestCase):
    def test_vertical_pipe_transition_retains_ring_phase(self):
        points = [[-.02,.19,-.18],[.14,.19,-.18],[.18,.23,-.18],[.18,.36,-.18]]
        frames = pipe_frames(points, [0,1,0])
        for u, v in frames:
            np.testing.assert_allclose(u, [0,0,1], atol=1e-12)
            self.assertAlmostEqual(0, np.dot(u,v))
            self.assertAlmostEqual(1, np.linalg.norm(v))

    def test_pipe_with_parallel_initial_reference_has_a_valid_frame(self):
        for u, v in pipe_frames([[0,0,0],[0,1,0],[0,2,0]], [0,1,0]):
            self.assertAlmostEqual(1, np.linalg.norm(u))
            self.assertAlmostEqual(1, np.linalg.norm(v))
            self.assertAlmostEqual(0, np.dot(u,v))

    def test_repeated_pipe_points_are_rejected(self):
        with self.assertRaises(ValueError):
            pipe_frames([[0,0,0],[0,0,0]], [0,1,0])

    def test_artillery_inset_stays_inside_and_sidewalls_are_convex(self):
        outer = np.array([[-2.9,.035],[2.58,.035],[2.98,.43],[2.61,.86],[-2.63,.86],[-3.06,.44]])
        inner = inset_profile(outer, [.075,.075])
        self.assertFalse(np.array_equal(inner, outer - np.sign(outer-outer.mean(0))*.075))
        for i in range(len(outer)):
            j=(i+1)%len(outer)
            q=np.array([outer[i],outer[j],inner[j],inner[i]])
            edges=np.roll(q,-1,axis=0)-q
            turns=edges[:,0]*np.roll(edges,-1,axis=0)[:,1]-edges[:,1]*np.roll(edges,-1,axis=0)[:,0]
            self.assertTrue(np.all(turns>0))
        np.testing.assert_allclose(inset_profile(outer[::-1],[.075,.075]),inner[::-1],atol=1e-12)

    def test_valid_tank_profile_is_byte_exact(self):
        outer=np.array([[-.418,-.27],[-.33,-.128],[.312,-.128],[.397,-.27],[.319,-.494],[-.34,-.494]])
        expected=outer-np.sign(outer-outer.mean(0))*[.014,.025]
        self.assertTrue(np.array_equal(expected,inset_profile(outer,[.014,.025])))

    def test_collapsed_inset_is_rejected(self):
        with self.assertRaises(ValueError):
            inset_profile([[0,0],[1,0],[1,1],[0,1]],[2,2])

    def test_display_images_use_the_runtime_linear_contract(self):
        with tempfile.TemporaryDirectory() as root:
            p=Path(root)
            Image.new('RGBA',(2,2),(128,128,128,255)).save(p/'ui.png')
            (p/'ui.asset.json').write_text(json.dumps(dict(id='ui.fixture',type='texture',source='ui.png',textureUsage='displayImage',textureColorSpace='linear')))
            self.assertEqual(0,audit.audit(p)['summary']['errors'])

    def test_animation_only_source_is_not_a_missing_mesh(self):
        raw=struct.pack('<8f',0,1,0,0,0,1,0,0)
        g={'nodes':[{}],'accessors':[{'bufferView':0,'componentType':5126,'count':2,'type':'SCALAR'},
           {'bufferView':1,'componentType':5126,'count':2,'type':'VEC3'}],
           'bufferViews':[{'buffer':0,'byteOffset':0,'byteLength':8},{'buffer':0,'byteOffset':8,'byteLength':24}],
           'animations':[{'samplers':[{'input':0,'output':1}], 'channels':[{'sampler':0,'target':{'node':0,'path':'translation'}}]}]}
        stats,issues=animation_stats(g,[raw],audit.accessor)
        self.assertEqual({'animations':1,'channels':1},stats)
        self.assertEqual([],issues)
        g['animations'][0]['channels'][0]['target']['node']=1
        with self.assertRaisesRegex(ValueError,'node'):
            animation_stats(g,[raw],audit.accessor)


if __name__ == '__main__':
    unittest.main()
