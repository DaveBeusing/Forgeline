import base64,copy,json,tempfile,unittest
from pathlib import Path
from baseline_contracts import canonical,sha256_bytes
from world_input_diagnostics import read_inspector,validate_series,compare_inspector_lanes,FACT_SCHEMA
from stage_diagnostics import compare_series

def default_typed(schema):
    if schema['nullable']:return None
    kind=schema['kind']
    if kind=='object':return {k:default_typed(v) for k,v in schema['properties'].items()}
    if kind=='enum':return schema['values'][0]['name']
    return {'integer':0,'number':0.0,'bool':False,'string':''}[kind]
def public_facts(stage):
    value=default_typed(FACT_SCHEMA);value['terrain']['isPrepared']=True;value['instances']['assetsPrepared']=True
    value['graphics']['device']['adapterName']='synthetic CPU fixture'
    value['graphics']['surface'].update(width=stage['outputRaster']['width'],height=stage['outputRaster']['height'],bufferCount=3,frameIndex=(stage['frameIndex']+1)%3,presentedFrameCount=stage['presentedAfter'],submittedFrameCount=stage['submission']['submission'],presentMode='fixture')
    value.update(lastSubmission=copy.deepcopy(stage['submission']),deviceIdentity=copy.deepcopy(stage['deviceIdentity']),inputFingerprint=stage['inputFingerprint'])
    timings={}
    def split(node,prefix=''):
        if type(node) is not dict:return
        for key in list(node):
            path=prefix+'.'+key if prefix else key
            if key.endswith('Milliseconds'):timings[path]=node.pop(key)
            else:split(node[key],path)
    split(value);return value,timings

def make_record(stage,stage_ref,previous_stage=None):
    rep=stage['repetition'];host=stage['producer']=='actual-render-host';sites=['HostReadbackPrepare'] if host else ['NativeFixedPoseRestore','CaptureEnsurePrepared'] if rep==0 else ['CaptureEnsurePrepared']
    base=rep*3 if host else 0 if rep==0 else 5+(rep-1)*3;rows=[]
    for i in range(2*len(sites)+1):
        final=i==2*len(sites);index=base+i
        state,timings=public_facts(stage)
        rows.append(dict(index=index,repetition=rep,siteOrdinal=i//2,site=None if final else sites[i//2],phase='AfterCaptureAndClientRetire' if final else 'BeforeSite' if i%2==0 else 'AfterSite',preparationInvoked=None if final or i%2==0 else sites[i//2]!='CaptureEnsurePrepared',readStartTick=index*10,readEndTick=index*10+1,managedOwnerThread=11,nativeOwnerThread=12,camera=stage['camera'],stateAndActivityFacts=state,timingFacts=timings))
    for row in rows[:-1]:
        state=row['stateAndActivityFacts'];state['graphics']['surface'].update(frameIndex=stage['frameIndex'],presentedFrameCount=stage['presentedBefore'],submittedFrameCount=stage['submission']['submission']-1)
        state['lastSubmission']=copy.deepcopy(previous_stage['submission']) if previous_stage is not None else dict(stage['submission'],submission=stage['submission']['submission']-1,submittedFence=stage['submission']['submittedFence']-3 if stage['submission']['submission']>1 else 0)
    profile=canonical(stage['effectiveProfile']);captured={k:stage[k] for k in ('submission','completedFence','frameIndex','presentedBefore','presentedAfter','deviceIdentity')};captured['bufferCount']=3
    return dict(schemaVersion=1,kind='reference-world-input-inspector',qualified=False,complete=True,failure=None,producer=stage['producer'],sourceSha=stage['sourceSha'],runtimeFingerprint=stage['runtimeFingerprint'],recipeFingerprint=stage['recipeFingerprint'],recipeInputBytesBase64=stage['recipeInputBytesBase64'],profileDomain="effective-quality-profile",profileFingerprint=sha256_bytes(profile),profileInputBytesBase64=base64.b64encode(profile).decode(),processId=stage['deviceIdentity']['processId'],managedOwnerThread=11,nativeOwnerThread=12,repetition=rep,declaredRepeats=8,capacity=40,stageDiagnostics=dict(file=Path(stage_ref['path']).name,sha256=stage_ref['sha256']),captured=captured,observations=rows)
class WorldInputDiagnosticsTests(unittest.TestCase):
    def setUp(self):
        # Reuse the original raw-stage CPU fixture, not a relabelled native execution.
        from test_stage_diagnostics import StageDiagnosticsTests
        self.fixture=StageDiagnosticsTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups);self.root=self.fixture.root;self.stage_refs=self.fixture.refs;self.refs=[]
        for i,ref in enumerate(self.stage_refs):self.save(i,make_record(self.fixture.record(i),ref,self.fixture.record(i-1) if i else None))
    def save(self,i,record):
        raw=canonical(record);name=f'{i}-world.json';(self.root/name).write_bytes(raw);ref=dict(path=name,sha256=sha256_bytes(raw))
        if i<len(self.refs):self.refs[i]=ref
        else:self.refs.append(ref)
    def record(self,i):return json.loads((self.root/self.refs[i]['path']).read_bytes())
    def one(self,i=0):return read_inspector(self.root,self.refs[i],self.stage_refs[i],self.fixture.record(i))
    def test_complete_native_series_and_optional_stage_comparator(self):
        result=validate_series(self.root,self.stage_refs,self.refs);self.assertTrue(result['available']);self.assertFalse(result['qualified']);self.assertEqual(self.one()['record']['observations'][2]['preparationInvoked'],None)
        self.assertTrue(compare_series(self.root,self.stage_refs,inspector_references=self.refs,inspector_version=1)['worldInputInspector']['available'])
        self.assertIsNone(self.one()['record']['observations'][-1]['stateAndActivityFacts']['logicalMemory'])
    def test_historical_absence_is_unavailable_but_selected_absence_cannot_downgrade(self):
        self.assertFalse(read_inspector(self.root,None,self.stage_refs[0],self.fixture.record(0),None)['available'])
        self.assertFalse(compare_series(self.root,self.stage_refs)['worldInputInspector']['available'])
        for refs,version in [(None,1),(self.refs[:-1],1),(self.refs,True),(self.refs,2)]:
            with self.assertRaises(ValueError):compare_series(self.root,self.stage_refs,inspector_references=refs,inspector_version=version)
    def test_original_hash_and_stage_reference_tamper_reject(self):
        original=self.record(0)
        for change in [lambda r:r.update(profileFingerprint='0'*64),lambda r:r['stageDiagnostics'].update(sha256='0'*64),lambda r:r['stageDiagnostics'].update(file='../0.json'),lambda r:r.update(recipeFingerprint='0'*64),lambda r:r['captured']['submission'].update(resourceGeneration=99),lambda r:r['observations'][-2]['stateAndActivityFacts']['graphics']['surface'].update(frameIndex=(r['captured']['frameIndex']+1)%3)]:
            r=copy.deepcopy(original);change(r);self.save(0,r)
            with self.assertRaises(ValueError):self.one()
        self.save(0,original);(self.root/self.refs[0]['path']).write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError,'hash'):self.one()
    def test_missing_duplicate_reordered_foreign_owner_and_predicate_phases_reject(self):
        original=self.record(0)
        for change in [lambda r:r['observations'].pop(),lambda r:r['observations'].reverse(),lambda r:r['observations'][1].update(phase='BeforeSite'),lambda r:r['observations'][0].update(preparationInvoked=False),lambda r:r['observations'][1].update(preparationInvoked=False),lambda r:r['observations'][2].update(nativeOwnerThread=13),lambda r:r['observations'][2].update(readStartTick=0),lambda r:r.update(complete=False),lambda r:r.update(schemaVersion=True)]:
            r=copy.deepcopy(original);change(r);self.save(0,r)
            with self.assertRaises(ValueError):self.one()
    def test_lag_and_cached_nulls_remain_available_but_missing_groups_or_fake_sentinels_reject(self):
        original=self.record(0);self.assertTrue(self.one()['available'])
        for change in [lambda r:r['observations'][0]['stateAndActivityFacts'].pop('logicalMemory'),lambda r:r['observations'][0]['stateAndActivityFacts'].update(logicalMemory=0),lambda r:r['observations'][0]['stateAndActivityFacts']['terrain']['last'].update(cpuSubmissionMilliseconds=0),lambda r:r['observations'][0]['timingFacts'].update({'invalid':1}),lambda r:r['observations'][-1]['stateAndActivityFacts']['graphics']['surface'].update(presentedFrameCount=99)]:
            r=copy.deepcopy(original);change(r);self.save(0,r)
            with self.assertRaises(ValueError):self.one()
    def test_all_typed_leaves_and_required_nullable_timing_paths_reject_missing_and_bool_aliases(self):
        original=self.record(0)
        mutations=[lambda r:r['observations'][0]['stateAndActivityFacts']['terrain'].update(work={'x':0}),lambda r:r['observations'][0]['stateAndActivityFacts']['terrain']['work'].update(meshBuilds=True),lambda r:r['observations'][0]['stateAndActivityFacts']['instances']['submission'].update(skinUploadBytes=True),lambda r:r['observations'][0]['stateAndActivityFacts']['particles'].update(activeParticles=True),lambda r:r['observations'][0]['stateAndActivityFacts']['graphics']['resources'].update(loadedTextureCount=True),lambda r:r['observations'][0]['stateAndActivityFacts']['graphics']['health'].update(liveResourceCount=True),lambda r:r['observations'][0]['stateAndActivityFacts']['graphics']['debug'].update(enabled=0),lambda r:r['observations'][0]['timingFacts'].pop('graphics.gpuFrameMilliseconds'),lambda r:r['observations'][0].update(timingFacts={})]
        for mutation in mutations:
            r=copy.deepcopy(original);mutation(r);self.save(0,r)
            with self.subTest(mutation=mutation),self.assertRaises(ValueError):self.one()
    def test_all_available_pre_phase_identity_surface_and_prior_submission_joins(self):
        original=self.record(0)
        for change in [lambda r:r['observations'][0]['stateAndActivityFacts']['deviceIdentity'].update(processId=99),lambda r:r['observations'][0]['stateAndActivityFacts']['graphics']['surface'].update(width=99),lambda r:r['observations'][0]['stateAndActivityFacts']['lastSubmission'].update(generation=99),lambda r:r['observations'][-2]['stateAndActivityFacts']['lastSubmission'].update(submission=99),lambda r:r['observations'][0]['stateAndActivityFacts']['graphics']['surface'].update(bufferCount=2)]:
            r=copy.deepcopy(original);change(r);self.save(0,r)
            with self.subTest(change=change),self.assertRaises(ValueError):self.one()
    def test_every_selected_ref_and_inter_repeat_chronology_is_required(self):
        for refs in [self.refs[:-1],self.refs[:7]+self.refs[:1],self.refs[1:]+self.refs[:1]]:
            with self.assertRaises(ValueError):validate_series(self.root,self.stage_refs,refs)
        r=self.record(1)
        for row in r['observations']:row.update(readStartTick=0,readEndTick=1)
        self.save(1,r)
        with self.assertRaises(ValueError):validate_series(self.root,self.stage_refs,self.refs)
    def test_host_joins_own_different_process_generations_without_cross_lane_equality(self):
        refs=self.fixture.host_records();inspectors=[]
        for i,ref in enumerate(refs):
            stage=json.loads((self.root/ref['path']).read_bytes());raw=canonical(make_record(stage,ref,json.loads((self.root/refs[i-1]['path']).read_bytes()) if i else None));name=f'host-{i}-world.json';(self.root/name).write_bytes(raw);inspectors.append(dict(path=name,sha256=sha256_bytes(raw)))
        self.assertTrue(validate_series(self.root,refs,inspectors)['available']);self.assertTrue(validate_series(self.root,self.stage_refs,self.refs)['available'])
        self.assertFalse(compare_inspector_lanes(self.root,self.stage_refs,self.refs,refs,inspectors)['qualified'])
        with self.assertRaises(ValueError):validate_series(self.root,refs,self.refs)
if __name__=='__main__':unittest.main()
