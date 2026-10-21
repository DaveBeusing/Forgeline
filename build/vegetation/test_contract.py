"""Portable checks against the retained plant exports and canonical material closure."""
import json
import math
import sys
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog, geometry_stats, read_geometry
import retained


class VegetationContractTests(unittest.TestCase):
    def test_retained_candidate_is_source_bound(self):
        retained.validate()

    def test_rooted_geometry_and_lod_budgets(self):
        catalog = Catalog(ROOT / 'assets/source')
        for name, counts in {'conifer': (80,48), 'broadleaf': (624,152), 'scrub': (40,24), 'grass_clump': (12,6)}.items():
            for low in (False, True):
                aid = 'mesh.world.vegetation.' + name + ('_lod1' if low else '')
                with self.subTest(asset=aid):
                    definition = catalog.definitions[aid][1]
                    geometry = read_geometry(retained.TARGET / (aid + '.glb'), retained.TARGET, definition)
                    stats = geometry_stats(geometry)
                    self.assertEqual(counts[int(low)], stats['triangles'])
                    self.assertEqual(['material.world.vegetation'], definition['materialReferences'])
                    self.assertAlmostEqual(0, stats['boundsMin'][1], places=6)
                    self.assertAlmostEqual(1, stats['boundsMax'][1], places=6)
                    self.assertGreater(stats['boundsMax'][2] - stats['boundsMin'][2], .8)
                    for axis in (0,2):
                        self.assertGreaterEqual(stats['boundsMin'][axis], -.500001)
                        self.assertLessEqual(stats['boundsMax'][axis], .500001)
                    for p in geometry['primitives']:
                        for normal in p['attributes']['NORMAL']:
                            self.assertAlmostEqual(1, math.sqrt(sum(v*v for v in normal)), places=5)

    def test_conifer_bark_and_foliage_use_existing_cells(self):
        catalog = Catalog(ROOT / 'assets/source')
        for low, sides in ((False,10), (True,6)):
            aid = 'mesh.world.vegetation.conifer' + ('_lod1' if low else '')
            g = read_geometry(retained.TARGET / (aid + '.glb'), retained.TARGET, catalog.definitions[aid][1])
            p = g['primitives'][0]
            for offset, index in enumerate(p['indices']):
                uv = p['attributes']['TEXCOORD_0'][index]
                col, row = (2,2) if offset < sides*6 else (0,3)
                for value, cell in zip(uv, (col,row)):
                    self.assertGreaterEqual(value, cell * .25 + .017999)
                    self.assertLessEqual(value, (cell + 1) * .25 - .017999)

    def test_shrub_lobes_are_closed_and_outward_facing(self):
        from collections import Counter
        catalog = Catalog(ROOT / 'assets/source')
        for low in (False, True):
            aid = 'mesh.world.vegetation.scrub' + ('_lod1' if low else '')
            g = read_geometry(retained.TARGET / (aid + '.glb'), retained.TARGET, catalog.definitions[aid][1])
            points = g['primitives'][0]['attributes']['POSITION']
            for start in range(0, len(points), 24):
                edges = Counter()
                volume = 0
                for i in range(start, start+24, 3):
                    a,b,c = points[i:i+3]
                    for u,v in ((a,b),(b,c),(c,a)):
                        edges[(u,v)] += 1
                    volume += (a[0]*(b[1]*c[2]-b[2]*c[1])+a[1]*(b[2]*c[0]-b[0]*c[2])+a[2]*(b[0]*c[1]-b[1]*c[0]))/6
                self.assertGreater(volume, 0)
                self.assertTrue(all(count == 1 and edges[(b,a)] == 1 for (a,b),count in edges.items()))

    def test_folded_grass_keeps_the_same_roots_and_tips_across_lod(self):
        catalog = Catalog(ROOT / 'assets/source')
        geometries = []
        for low in (False, True):
            aid = 'mesh.world.vegetation.grass_clump' + ('_lod1' if low else '')
            g = read_geometry(retained.TARGET / (aid + '.glb'), retained.TARGET, catalog.definitions[aid][1])
            bounds = geometry_stats(g)
            pts = g['primitives'][0]['attributes']['POSITION']
            geometries.append([tuple((p[i]-bounds['boundsMin'][i])/(bounds['boundsMax'][i]-bounds['boundsMin'][i]) for i in range(3)) for p in pts])
        for blade in range(3):
            for high,low in ((0,0),(1,1),(8,2)):
                for a,b in zip(geometries[0][blade*12+high],geometries[1][blade*6+low]):
                    self.assertAlmostEqual(a,b,places=6)

    def test_material_maps_keep_semantic_colorspaces_and_caps(self):
        catalog = Catalog(ROOT / 'assets/source')
        material = json.loads(catalog.source('material.world.vegetation').read_text(encoding='utf-8-sig'))
        for key, usage, color in [('baseColorTexture','baseColor','srgb'), ('normalTexture','normal','linear'), ('ormTexture','orm','linear')]:
            aid = material[key]
            self.assertTrue(aid.startswith('texture.directorate.surface.atlas_'))
            definition = catalog.definitions[aid][1]
            self.assertEqual(usage, definition['textureUsage'])
            self.assertEqual(color, definition['textureColorSpace'])
            self.assertGreater(definition['textureMaxDimension'], 0)
        self.assertNotIn('emissiveTexture', material)


if __name__ == '__main__':
    unittest.main()
