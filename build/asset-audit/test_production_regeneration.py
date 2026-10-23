"""Production exporters must reproduce the repaired families without source writes."""
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import numpy as np
import source_audit as audit

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'assets/source'


class ProductionRegenerationTests(unittest.TestCase):
    def test_repaired_exports_and_hashes_match_current_authoring(self):
        families = [
            ('Author-IndustrialBuildings.py', 'smelter', 'buildings/directorate/sources/industrial'),
            ('Author-CombatSupportUnits.py', 'self_propelled_artillery', 'units/directorate/sources/combat_support'),
        ]
        with tempfile.TemporaryDirectory(prefix='forgeline-geometry-') as temporary:
            for script, family, folder in families:
                with self.subTest(family=family):
                    canonical = SOURCE / folder / family
                    original_hashes = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in canonical.rglob('*') if p.is_file()}
                    subprocess.run([sys.executable, str(ROOT / 'build' / script), '--family', family,
                                    '--output', temporary], check=True, capture_output=True, text=True, timeout=30)
                    exported = Path(temporary) / family
                    expected = {p.relative_to(canonical) for p in canonical.rglob('*.gltf')}
                    actual = {p.relative_to(exported) for p in exported.rglob('*.gltf')}
                    self.assertTrue(expected)
                    self.assertEqual(expected, actual)
                    report = json.loads((canonical / 'exports.json').read_text())
                    records = report.get('meshes', report)
                    for relative in sorted(expected):
                        original, buffers = audit.load_mesh(canonical / relative, SOURCE)
                        regenerated, regenerated_buffers = audit.load_mesh(exported / relative, exported)
                        left, right = copy.deepcopy(original), copy.deepcopy(regenerated)
                        left.pop('buffers'); right.pop('buffers')
                        self.assertEqual(left, right, str(relative))
                        for index, accessor in enumerate(original['accessors']):
                            a = audit.accessor(original, buffers, index)
                            b = audit.accessor(regenerated, regenerated_buffers, index)
                            if accessor['componentType'] == 5126:
                                # Windows/Linux libm can differ at effectively zero normals.
                                np.testing.assert_allclose(a, b, rtol=0, atol=1e-6, err_msg=str(relative))
                            else:
                                np.testing.assert_array_equal(a, b, err_msg=str(relative))
                        self.assertEqual([], audit.mesh_stats(original, buffers)[1], str(relative))
                        self.assertEqual(records[relative.with_suffix('').as_posix()]['sha256'],
                                         original_hashes[canonical / relative], str(relative))
                    self.assertEqual(original_hashes, {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in original_hashes})


if __name__ == '__main__':
    unittest.main()
