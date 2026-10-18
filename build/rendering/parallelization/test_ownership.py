import copy,json,re,hashlib,unittest
from pathlib import Path
from ValidateOwnership import validate_manifest, validate_diff, ValidationError
ROOT=Path(__file__).resolve().parents[3]
MANIFEST=ROOT/'docs/rendering/parallelization/ParallelRenderOwnership.json'

class OwnershipTests(unittest.TestCase):
    def setUp(self):self.manifest=json.loads(MANIFEST.read_text(encoding='utf-8-sig'))
    def test_eight_disjoint_lanes_and_current_paths(self):
        lanes=validate_manifest(self.manifest)
        self.assertEqual(8,sum(x['phase']=='parallel' for x in lanes.values()))
        for lane in lanes.values():
            if lane['phase']!='parallel':continue
            for path in lane['writeExact']:
                self.assertTrue((ROOT/path).is_file() or path in self.manifest['plannedNewExact'],path)
    def test_duplicate_case_parent_overlap_and_frozen_writes_fail(self):
        for mutation in ('duplicate','parent','frozen','shared'):
            d=copy.deepcopy(self.manifest);lanes=[x for x in d['lanes'] if x['phase']=='parallel']
            if mutation=='duplicate':lanes[0]['writeExact'].append(lanes[0]['writeExact'][0].upper())
            if mutation=='parent':lanes[0]['writePrefixes'].append('src/ForgeLine.Presentation/')
            if mutation=='frozen':lanes[0]['writeExact'].append('Directory.Build.props')
            if mutation=='shared':lanes[1]['writeExact'].append(lanes[0]['writeExact'][0])
            with self.subTest(mutation=mutation),self.assertRaises(ValidationError):validate_manifest(d)
    def test_lane_diff_cannot_change_neighbor_or_serial_dispatcher(self):
        lanes=validate_manifest(self.manifest);lane=next(x for x in lanes.values() if x['phase']=='parallel')
        self.assertEqual(1,validate_diff(self.manifest,lanes,lane['name'],[lane['writeExact'][0]]))
        for path in ('Directory.Build.props','../outside','src/ForgeLine.Client/ClientRenderHost.cs'):
            with self.subTest(path=path),self.assertRaises(ValidationError):validate_diff(self.manifest,lanes,lane['name'],[path])
    def test_pipeline_factories_are_mechanically_identical_to_the_frozen_baseline(self):
        expected=json.loads((MANIFEST.parent/'FactorySources.json').read_text())
        actual={}
        for source,target in [('SimpleInstanceRenderer','InstanceMaterialPipeline'),('TerrainRenderer','TerrainMaterialPipeline')]:
            text=(ROOT/f'src/ForgeLine.Presentation/{target}.cs').read_text()
            pattern=r'^    internal static IGraphicsPipeline (\w+)\(.*?(?=^    (?:internal|private|public) |^})'
            for m in re.finditer(pattern,text,re.M|re.S):
                body=m.group().replace('internal static IGraphicsPipeline','private static IGraphicsPipeline',1)
                actual[source+'.'+m[1]]=hashlib.sha256(body.encode()).hexdigest()
        self.assertEqual({x['method']:x['sha256'] for x in expected},actual)

if __name__=='__main__':unittest.main()
