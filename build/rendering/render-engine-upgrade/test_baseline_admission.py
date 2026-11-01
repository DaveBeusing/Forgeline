import base64,copy,json,subprocess,tempfile,unittest
from pathlib import Path
from datetime import datetime,timezone
import baseline_admission as a
from baseline_contracts import canonical,ContractError,sha256_bytes

class BaselineAdmissionContracts(unittest.TestCase):
    def test_git_deadline_cannot_admit_or_escape_as_unretained_timeout(self):
        from unittest.mock import patch
        with patch.object(a.subprocess,'run',side_effect=subprocess.TimeoutExpired(['git'],30)):
            with self.assertRaisesRegex(ValueError,'timed out'):a.git(Path('.'),'rev-parse','HEAD')
    def test_complete_matrix_has_cold_and_scaled_cases(self):
        cases=a.required_cases();self.assertEqual(len(cases),230)
        self.assertIn(('IndustrialBase','Medium','NormalGameplay',1920,1080,'RendererCold','Fixed'),cases)
        self.assertIn(('Scaling100000High','High','Strategic',1920,1080,'Warm','Sweep'),cases)
    def test_one_to_one_completed_tuple_consumption_and_all_negative_joins(self):
        def row(i,fence):return dict(index=i,ordinal=64+i,generation=1,resourceGeneration=2,submittedFence=fence)
        cpu=[row(0,10),row(1,11)];a.exact_join(cpu,copy.deepcopy(cpu),copy.deepcopy(cpu),64)
        for defect in ('duplicateCpuAck','missing','generation','resource','index','ordinal','extra'):
            c,x,g=copy.deepcopy(cpu),copy.deepcopy(cpu),copy.deepcopy(cpu)
            if defect=='duplicateCpuAck':c[1]['submittedFence']=x[1]['submittedFence']=10
            elif defect=='missing':g.pop()
            elif defect=='generation':x[1]['generation']=3
            elif defect=='resource':x[1]['resourceGeneration']=3
            elif defect=='index':c[1]['index']=0
            elif defect=='ordinal':x[1]['ordinal']=64
            else:g.append(row(2,12))
            with self.subTest(defect=defect),self.assertRaises(ValueError):a.exact_join(c,x,g,64)
    def test_recipe_validates_exact_bytes_not_caller_hash_label(self):
        recipe={'position':{'x':1,'y':2},'generation':3};raw=json.dumps(recipe).encode();case=dict(recipe=recipe,recipeFingerprint=sha256_bytes(raw))
        value=dict(recipeInputs=recipe,recipeFingerprint=case['recipeFingerprint'],recipeInputBytesBase64=base64.b64encode(raw).decode())
        a.validate_recipe(value,case)
        for mutation in ('bytes','parsed','digest'):
            v=copy.deepcopy(value)
            if mutation=='bytes':v['recipeInputBytesBase64']=base64.b64encode(b'{}').decode()
            elif mutation=='parsed':v['recipeInputs']['position']['x']=5
            else:v['recipeFingerprint']='b'*64
            with self.assertRaises(ValueError):a.validate_recipe(v,case)
    def test_symmetric_15percent_control_and_zero_boundary(self):
        a.stable_pair(100,115);a.stable_pair(115,100);a.stable_pair(0,0)
        for x,y in ((100,115.01),(115.01,100),(0,1),(1,0),(float('nan'),1),(True,1)):
            with self.subTest(x=x,y=y),self.assertRaises(ValueError):a.stable_pair(x,y)
    def test_policy_all_actual_counters_and_review_predates_capture(self):
        time=datetime(2026,10,30,tzinfo=timezone.utc)
        p=dict(schemaVersion=1,kind='gpu-memory-budget',reviewer='reviewed-engineer',rationale='Finite capacity ledger',reviewedAtUtc='2026-10-29T00:00:00Z',maximumPerSample={k:1000 for k in a.MEMORY_COUNTERS},assumptions=dict(frameBufferCount=3,maximumExpandedDrawnParts=100000,maximumHudVertices=100000))
        a.validate_policy(p,'gpu-memory-budget',time)
        for mutation in ('missingCounter','future','bool','kind'):
            v=copy.deepcopy(p)
            if mutation=='missingCounter':v['maximumPerSample'].pop('pendingUploadBytes')
            elif mutation=='future':v['reviewedAtUtc']='2026-10-31T00:00:00Z'
            elif mutation=='bool':v['maximumPerSample']['liveResourceBytes']=True
            else:v['kind']='physical-allocation-total'
            with self.assertRaises(ValueError):a.validate_policy(v,'gpu-memory-budget',time)
    def test_mixed_coverage_requires_same_frame_actual_material_skin_mechanics_and_drawn_effects(self):
        case=dict(scene='BattlefieldCombat',recipe=dict(kind='BattlefieldCombat'))
        actual=dict(generation=1,resourceGeneration=1,submittedFence=10,renderFrame=dict(startTick=100,endTick=200))
        content=dict(actualSubmission=actual,recordingEpoch=1,frameIndex=0,observerStartTick=120,observerEndTick=130,
            distinctRuntimeMaterials=2,skinnedInstances=1,mechanicalInstances=1,mainInstances=2,fallbackInstances=0)
        row=dict(actualSubmission=actual,submission=dict(content=content,particles=dict(activeParticles=1,drawCalls=1)))
        a.validate_mixed_consumption([row],case,'native')
        for defect in ('materials','skin','mechanical','fallback','effects','draws','staleFence','outside'):
            v=copy.deepcopy(row);c=v['submission']['content'];p=v['submission']['particles']
            if defect=='materials':c['distinctRuntimeMaterials']=1
            elif defect=='skin':c['skinnedInstances']=0
            elif defect=='mechanical':c['mechanicalInstances']=0
            elif defect=='fallback':c['fallbackInstances']=1
            elif defect=='effects':p['activeParticles']=0
            elif defect=='draws':p['drawCalls']=0
            elif defect=='staleFence':c['actualSubmission']=dict(actual,submittedFence=11)
            else:c['observerEndTick']=201
            with self.subTest(defect=defect),self.assertRaises(ValueError):a.validate_mixed_consumption([v],case,'native')
        left,right=copy.deepcopy(row),copy.deepcopy(row)
        left['submission']['content']['skinnedInstances']=0;right['submission']['content']['mechanicalInstances']=0;right['submission']['content']['recordingEpoch']=2
        with self.assertRaises(ValueError):a.validate_mixed_consumption([left,right],case,'native')
    def test_fixture_trace_is_not_owner_site_attribution(self):
        with tempfile.TemporaryDirectory() as root:
            p=Path(root)/'fixture.json';p.write_text('{"kind":"sampled-allocation-integration","status":"diagnostic-pass"}')
            with self.assertRaises(ValueError):a.validate_sampled_owner_diagnostic(root,dict(path='fixture.json',sha256=sha256_bytes(p.read_bytes())),'a'*40,'b'*64,'c'*64)
    def test_missing_or_wrong_cohort_is_not_admitted(self):
        for c in ({},dict(schemaVersion=True,kind='baseline-control-cohort')):
            with self.assertRaises(ValueError):a.evaluate(c,Path('.'),Path('.'),'a'*40)
    def test_accepted_record_is_git_master_bytes_not_external_manifest_shape(self):
        with tempfile.TemporaryDirectory() as root:
            root=Path(root)
            def git(*args):return subprocess.run(['git',*args],cwd=root,capture_output=True,check=True).stdout.decode().strip()
            git('init','-q');git('config','user.name','Contract test');git('config','user.email','contract@example.invalid')
            path=a.ROOT+'evidence/baseline/frozen.json';p=root/path;p.parent.mkdir(parents=True);p.write_bytes(b'{"kind":"accepted-test-record"}')
            git('add','.');git('commit','-qm','accepted record');sha=git('rev-parse','HEAD');ref=dict(path=path,sha256=sha256_bytes(p.read_bytes()))
            self.assertEqual(a.committed(root,sha,ref)['kind'],'accepted-test-record')
            p.write_bytes(b'{"kind":"self-authorized-tamper"}')
            with self.assertRaises(ValueError):a.committed(root,sha,dict(path=path,sha256=sha256_bytes(p.read_bytes())))
            with self.assertRaises(ValueError):a.committed(root,sha,dict(path='../elsewhere',sha256='a'*64))
    def test_hash_bound_files_reject_tamper_and_escape(self):
        with tempfile.TemporaryDirectory() as root:
            p=Path(root)/'value.json';p.write_bytes(b'{}');ref=dict(path='value.json',sha256=sha256_bytes(p.read_bytes()))
            self.assertEqual(a.load(root,ref),{})
            p.write_bytes(b'{"tamper":true}')
            with self.assertRaises(ValueError):a.load(root,ref)
            with self.assertRaises(ValueError):a.local(root,'../escape')

    def test_hash_bound_actual_request_provenance_is_independently_unadmittable(self):
        import test_baseline_producer_integration as integration
        from unittest.mock import patch
        original_put=integration.put
        for value in (1,2,0,False,'unsupported',{},[]):
            def put(root,name,record,raw=False):
                if name=='actual-request.json':record=dict(record,captureBoundInputProvenanceVersion=value)
                if name=='attempt.json':record=dict(record,diagnosticOnly=False)
                return original_put(root,name,record,raw)
            with self.subTest(value=value),tempfile.TemporaryDirectory() as root,patch.object(integration,'put',side_effect=put):
                fixture=integration.SyntheticProducerCohort(root)
                first=fixture.cohort['runs'][0];directory=Path(root)/first['directory']
                attempt=json.loads((directory/first['attempt']['path']).read_bytes())
                self.assertIs(attempt['diagnosticOnly'],False)
                self.assertEqual(attempt['requestSha256'],sha256_bytes((directory/first['request']['path']).read_bytes()))
                with self.assertRaisesRegex(ValueError,'CPU-source provenance'):fixture.evaluate()
    def test_absent_and_null_provenance_keep_existing_positive_admission_controls(self):
        import test_baseline_producer_integration as integration
        from unittest.mock import patch
        original_put=integration.put
        for explicit_null in (False,True):
            def put(root,name,record,raw=False):
                if name=='actual-request.json' and explicit_null:record=dict(record,captureBoundInputProvenanceVersion=None)
                return original_put(root,name,record,raw)
            with self.subTest(explicit_null=explicit_null),tempfile.TemporaryDirectory() as root,patch.object(integration,'put',side_effect=put):
                self.assertTrue(integration.SyntheticProducerCohort(root).evaluate()['baselineControlsAccepted'])
    def test_hash_bound_actual_request_post_frame_snapshot_is_independently_unadmittable(self):
        import test_baseline_producer_integration as integration
        from unittest.mock import patch
        original_put=integration.put
        for value in (1,2,0,False,'unsupported',{},[]):
            def put(root,name,record,raw=False):
                if name=='actual-request.json':record=dict(record,capturePostFrameResourceSnapshotVersion=value)
                if name=='attempt.json':record=dict(record,diagnosticOnly=False)
                return original_put(root,name,record,raw)
            with self.subTest(value=value),tempfile.TemporaryDirectory() as root,patch.object(integration,'put',side_effect=put):
                fixture=integration.SyntheticProducerCohort(root)
                first=fixture.cohort['runs'][0];directory=Path(root)/first['directory']
                attempt=json.loads((directory/first['attempt']['path']).read_bytes())
                self.assertIs(attempt['diagnosticOnly'],False)
                self.assertEqual(attempt['requestSha256'],sha256_bytes((directory/first['request']['path']).read_bytes()))
                with self.assertRaisesRegex(ValueError,'post-frame snapshot'):fixture.evaluate()
    def test_absent_and_null_post_frame_snapshot_keep_existing_positive_admission_controls(self):
        import test_baseline_producer_integration as integration
        from unittest.mock import patch
        original_put=integration.put
        for explicit_null in (False,True):
            def put(root,name,record,raw=False):
                if name=='actual-request.json' and explicit_null:record=dict(record,capturePostFrameResourceSnapshotVersion=None)
                return original_put(root,name,record,raw)
            with self.subTest(explicit_null=explicit_null),tempfile.TemporaryDirectory() as root,patch.object(integration,'put',side_effect=put):
                self.assertTrue(integration.SyntheticProducerCohort(root).evaluate()['baselineControlsAccepted'])
if __name__=='__main__':unittest.main()
