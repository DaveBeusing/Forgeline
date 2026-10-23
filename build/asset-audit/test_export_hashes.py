"""Retained export reports must describe repository bytes, independent of host OS."""
import ast
import hashlib
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
AUTHORS = ('CommandCore', 'IndustrialBuildings', 'LogisticsBuildings', 'OperationalBuildings',
           'MainBattleTank', 'CombatSupportUnits', 'InfantryBattlefield', 'EnvironmentKit')


class ExportHashTests(unittest.TestCase):
    def test_retained_export_hashes_identify_current_source_bytes(self):
        source = ROOT / 'assets/source'
        hashes = {hashlib.sha256(p.read_bytes()).hexdigest() for p in source.rglob('*.gltf')}
        checked = 0
        for path in sorted(source.rglob('*exports.json')):
            report = json.loads(path.read_text(encoding='utf-8'))
            for key, entry in report.get('meshes', report).items():
                if not isinstance(entry, dict) or 'sha256' not in entry:
                    continue
                checked += 1
                self.assertIn(entry['sha256'], hashes, f'{path.relative_to(source)}: {key}')
        self.assertGreater(checked, 0)

    def test_hash_producing_authoring_paths_write_explicit_lf(self):
        for name in AUTHORS:
            path = ROOT / 'build' / f'Author-{name}.py'
            calls = [node for node in ast.walk(ast.parse(path.read_text(encoding='utf-8')))
                     if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)
                     and node.func.attr == 'write_text']
            self.assertTrue(calls, name)
            for node in calls:
                newline = next((k.value for k in node.keywords if k.arg == 'newline'), None)
                self.assertIsInstance(newline, ast.Constant, f'{name}:{node.lineno}')
                self.assertEqual('\n', newline.value, f'{name}:{node.lineno}')


if __name__ == '__main__':
    unittest.main()
