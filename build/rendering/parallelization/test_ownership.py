import copy,json,re,hashlib,unittest,subprocess
from functools import lru_cache
from pathlib import Path
from ValidateOwnership import validate_manifest, validate_diff, ValidationError
ROOT=Path(__file__).resolve().parents[3]
MANIFEST=ROOT/'docs/rendering/parallelization/ParallelRenderOwnership.json'


FACTORIES=(('SimpleInstanceRenderer','InstanceMaterialPipeline'),('TerrainRenderer','TerrainMaterialPipeline'))

@lru_cache(maxsize=4)
def certified_sources(revision):
    result={}
    for _,name in FACTORIES:
        raw=subprocess.check_output(['git','show',f'{revision}:src/ForgeLine.Presentation/{name}.cs'],cwd=ROOT)
        result[name]=raw.decode('utf-8-sig').replace('\r\n','\n')
    return result

def closing_brace(text,start):
    depth=0;i=start
    while i<len(text):
        if text.startswith('//',i):
            end=text.find('\n',i+2);i=len(text) if end<0 else end+1;continue
        if text.startswith('/*',i):
            end=text.find('*/',i+2)
            if end<0:raise ValueError('Unterminated shader comment')
            i=end+2;continue
        if text[i] in ('"',"'"):
            quote=text[i];i+=1
            while i<len(text) and text[i]!=quote:
                i+=2 if text[i]=='\\' else 1
            if i>=len(text):raise ValueError('Unterminated shader string')
            i+=1;continue
        if text[i]=='{':depth+=1
        elif text[i]=='}':
            depth-=1
            if depth==0:return i
        i+=1
    raise ValueError('Unterminated shader function')

def factory_interface_source(text):
    # Mask implementation statements only. Keep C# configuration, shader entry
    # signatures, structs, cbuffer fields and resource registers byte-for-byte.
    functions=re.compile(r'(?m)^[ \t]*(?:float(?:[1-4](?:x[1-4])?)?|VertexOutput|void)\s+[A-Za-z_]\w*\s*\([^;{}]*\)\s*(?::\s*[A-Za-z_]\w*)?\s*\{')
    def shader(match):
        source=match.group();pieces=[];cursor=0
        for function in functions.finditer(source):
            opening=function.end()-1
            if opening<cursor:continue
            end=closing_brace(source,opening)
            pieces.append(source[cursor:opening+1]);pieces.append(' /* implementation */ ')
            cursor=end
        pieces.append(source[cursor:]);return ''.join(pieces)
    return re.sub(r'""".*?"""',shader,text,flags=re.S)

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
        # These hashes certify the historical extraction, not every later
        # shading implementation in its explicitly assigned ownership lane.
        expected=json.loads((MANIFEST.parent/'FactorySources.json').read_text())
        sources=certified_sources(self.manifest['integratedFoundationSha'])
        actual={}
        for source,target in FACTORIES:
            pattern=r'^    internal static IGraphicsPipeline (\w+)\(.*?(?=^    (?:internal|private|public) |^})'
            for m in re.finditer(pattern,sources[target],re.M|re.S):
                body=m.group().replace('internal static IGraphicsPipeline','private static IGraphicsPipeline',1)
                actual[source+'.'+m[1]]=hashlib.sha256(body.encode()).hexdigest()
        self.assertEqual({x['method']:x['sha256'] for x in expected},actual)

    def test_current_factory_configuration_and_shader_interfaces_remain_frozen(self):
        sources=certified_sources(self.manifest['integratedFoundationSha'])
        for _,target in FACTORIES:
            current=(ROOT/f'src/ForgeLine.Presentation/{target}.cs').read_text(encoding='utf-8-sig')
            with self.subTest(factory=target):
                self.assertEqual(factory_interface_source(sources[target]),factory_interface_source(current))

    def test_shader_implementation_changes_preserve_the_interface_comparison(self):
        sample='const string shader = """\nstruct Input { float3 Position : POSITION; };\nfloat4 PSMain(Input input) : SV_Target0 { return 1; }\n""";'
        changed=sample.replace('return 1;','if (input.Position.x>0) { return 0; } return .5;')
        self.assertEqual(factory_interface_source(sample),factory_interface_source(changed))
        self.assertNotEqual(factory_interface_source(sample),factory_interface_source(sample.replace('PSMain(Input input)','PSMain(Other input)')))

    def test_register_layout_constant_format_and_factory_mutations_are_rejected(self):
        source=certified_sources(self.manifest['integratedFoundationSha'])['InstanceMaterialPipeline']
        mutations=(('register(b0)','register(b2)'),('register(t1)','register(t7)'),
            ('float3 CameraPosition;','float4 CameraPosition;'),('float3 Normal : NORMAL;','float4 Normal : NORMAL;'),
            ('float4 WorldRow0 : INSTANCEWORLD0;','float4 WorldRow0 : INSTANCEWORLD4;'),
            ('SceneLightingSettings.ShaderConstantCount + 5','SceneLightingSettings.ShaderConstantCount + 6'),
            ('GraphicsFrameTargetFormat.Rgba16Float','GraphicsFrameTargetFormat.Rgba8Unorm'),
            ('DepthEnabled =\n                    true','DepthEnabled =\n                    false'))
        for old,new in mutations:
            with self.subTest(mutation=old):
                self.assertIn(old,source)
                self.assertNotEqual(factory_interface_source(source),factory_interface_source(source.replace(old,new,1)))

    def test_nested_shader_comments_cannot_hide_following_bindings(self):
        sample='const string shader = """\nfloat4 PSMain() : SV_Target0 { /* } */ if (true) { return 1; } // {\nreturn 0; }\nTexture2D NormalTexture : register(t1);\n""";'
        self.assertEqual(factory_interface_source(sample),factory_interface_source(sample.replace('return 1;','return .5;')))
        self.assertNotEqual(factory_interface_source(sample),factory_interface_source(sample.replace('register(t1)','register(t9)')))

    def test_unterminated_shader_implementation_is_rejected(self):
        with self.assertRaises(ValueError):factory_interface_source('const string shader = """\nfloat4 PSMain() : SV_Target0 { return 1;\n""";')

if __name__=='__main__':unittest.main()
