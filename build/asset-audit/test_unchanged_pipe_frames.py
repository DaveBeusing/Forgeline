import sys
from pathlib import Path
import unittest
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from source_geometry import pipe_frames


class PipeCompatibilityTests(unittest.TestCase):
    def test_regular_three_dimensional_frames_retain_exact_authored_roll(self):
        points=np.array([[-.2,-.08,-.07],[-.2,-.08,.02],[-.16,-.12,.06],[-.16,-.22,.11]])
        reference=np.array([0,1,0])
        result=pipe_frames(points,reference)
        for i,(u,v) in enumerate(result):
            tangent=points[min(i+1,len(points)-1)]-points[max(0,i-1)]
            tangent/=np.linalg.norm(tangent)
            expected=np.cross(tangent,reference);expected/=np.linalg.norm(expected)
            self.assertTrue(np.array_equal(u,expected))
            self.assertTrue(np.array_equal(v,np.cross(tangent,expected)))


if __name__=='__main__':unittest.main()
