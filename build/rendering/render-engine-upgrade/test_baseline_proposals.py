import base64,copy,json,tempfile,unittest
from pathlib import Path
from baseline_contracts import canonical,sha256_bytes
from baseline_admission import required_cases
from baseline_proposals import PROPOSAL,validate_complete_schema,validate_bound_proposal,validate_schedule,ordered_keys,payload,preflight

def reference(path):return dict(path=path,sha256='a'*64)

def profile_settings(quality):
    environment=dict(daylight=1,cloudCover=0,wetness=0);vegetation=dict(density=1,maximumDistanceMeters=4000);residency=dict(enabled=True,maximumResidentChunks=128)
    post=dict(antialiasing='Disabled',whiteBalance=0,tint=0,contrast=1,saturation=1,edgeBlend=0)
    requested=dict(quality=quality,colorGrade=post,resolutionScale=None,antialiasing=None,shadows=None,textures=None,objectDetail=None,surfaceDetail=None,vegetationMotion=None,atmosphere=None,particles=None,environment=environment,vegetation=vegetation,terrainResidency=residency)
    return dict(requested=quality,active=quality,linearScene=True,shadows=dict(quality='Low',enabled=True,resolution=1024,cascadeCount=1,radius=180,maximumCasters=1024,maximumDraws=256),atmosphere='Disabled',particles=dict(quality='Medium',capacity=2048,maximumFallbackEffects=64,maximumScreenCoverage=1),terrainDetail=dict(enabled=True,microTileMeters=4,microColorStrength=.04,microNormalStrength=.06,rockSlopeStart=.25,rockSlopeEnd=.6),lodDiameterScale=1,postProcessing=post,fallbackReason=None,resolutionScale=1,textures='Full',vegetationMotion=True,requestedSettings=requested,environment=environment,vegetation=vegetation,terrainResidency=residency)

def fixture():
    cases=[];camera=dict(target=dict(x=0,y=0,z=0),yawRadians=.5,pitchRadians=.5,distance=50,hasPointerPosition=False,pointerPosition=dict(x=0,y=0))
    for i,(scene,quality,zoom,width,height,run,path) in enumerate(sorted(required_cases())):
        samples=128 if run=='RendererCold' else 512;recipe=dict(kind=scene,display=dict(width=width,height=height),rasterDpi=96,cameraPath=path,samples=samples,runtimeFingerprint='b'*64);raw=canonical(recipe)
        cases.append(dict(id=f'case{i}',scene=scene,quality=quality,zoom=zoom,width=width,height=height,runProfile=run,cameraPath=path,rasterDpi=96,uiScenario='World',warmups=0 if run=='RendererCold' else 64,samples=samples,recipeFingerprint=sha256_bytes(raw),recipe=recipe,recipeInputBytesBase64=base64.b64encode(raw).decode(),initialCamera=camera,sampleCameras=[camera]*samples,effectiveProfile=profile_settings(quality),lighting=dict(directionToLight=dict(x=0,y=1,z=0),directionalIntensity=1,directionalColor=dict(x=1,y=1,z=1),ambientIntensity=.5,ambientColor=dict(x=1,y=1,z=1),exposure=1,groundAmbientFactor=.5,toneMapping='None'),atmosphere=dict(quality='Disabled',color=dict(x=0,y=0,z=0),density=0,startDistance=0,baseHeight=0,heightFalloff=0,maximumOpacity=0,zoomCompensation=0,localHaze=None,enabled=False),preflight=reference(f'preflight/{i}.json'),coverage='authored-world'))
    cases[0].update(coverage='mixed-material-skin-mechanics-effects',mixedEvidence=reference('mixed.json'))
    origin=dict(originVersion=1,kind='NewGame',load=None,smoke=True,declaredAdditionalInstances=0,seed=2026,scenarioKey='central-divide.directorate.v1',participants=[dict(player=1)],quality={},scenario={});raw=canonical(origin)
    extras=[dict(id='gameplay',mode='actual-live-gameplay',origin=origin,originFingerprint=sha256_bytes(raw),originInputBytesBase64=base64.b64encode(raw).decode(),originEvidence=reference('gameplay.json')),dict(id='observer',mode='observer-on-off',fixtureCaseId=next(c['id'] for c in cases if c['runProfile']=='Warm'))]
    rows=[dict(path='ForgeLine.Client.dll',bytes=1,sha256='a'*64)];source='a'*40
    protocol=dict(schemaVersion=1,kind='baseline-control-body-proposal',executionSource=source,frozenAtUtc=None,proposedAtUtc='2026-10-31T00:00:00Z',comparison='identical-instrumented-source-A/A',preserveAllAttempts=True,pairs=5,regressionPercent=15,rasterDpi=96,warmups=64,warmSamples=512,coldSamples=128,cases=cases,supplementalCoverage=extras,attemptInventoryRoot='attempts',launchOutputRoot='D:\\evidence\\frozen',payloadInventory=rows,nativeHardwareIdentitySha256='c'*64)
    by_id={c['id']:c for c in cases};keys=ordered_keys(5,by_id,{e['id']:e for e in extras})
    protocol['attemptSchedule']=[dict(pair=p,arm=a,caseId=c,producer=r,directory=f'attempts/{i:05}',launchOutputPath=f'D:\\evidence\\frozen\\{i:05}') for i,(p,a,c,r) in enumerate(keys)]
    sdk=canonical(dict(sdk=dict(version='synthetic-source-pin')));script=b'# synthetic schema-only source input'
    return dict(schemaVersion=1,kind=PROPOSAL,accepted=False,qualified=False,sourceInfo=dict(producerSourceSha=source,proposalToolSourceSha='d'*40,proposalToolSourceTreeClean=True,toolScript=dict(path='build/rendering/render-engine-upgrade/baseline_proposals.py',sha256=sha256_bytes(script),gitBlobSha='e'*40,bytesBase64=base64.b64encode(script).decode()),acceptedProtocolSourceSha=None,resultsReviewSourceSha=None),sourceSdkPin='synthetic-source-pin',sourceSdkBytesBase64=base64.b64encode(sdk).decode(),sourceSdkSha256=sha256_bytes(sdk),payloadInventory=rows,runtimeFingerprint='b'*64,nativeIdentityEvidence=reference('native.json'),protocol=protocol,planningEstimate=dict(producerSourceSha=source,basis='SYNTHETIC schema-only test; not a measured estimate',sourceReferences=[reference('estimate.json')],estimatedSecondsPerCommand=1,estimatedRetainedBytesPerCommand=1),policyReferences={k:reference('docs/rendering/render-engine-upgrade/policies/'+k+'.json') for k in ('hardware','allocation','gpuMemory')})

class CompleteProposalTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):cls.valid=fixture()
    def test_complete230_case4620_command_shape_stays_unaccepted_and_unbound(self):
        r=validate_complete_schema(self.valid);self.assertEqual(r['commands'],4620);self.assertFalse(r['accepted']);self.assertFalse(r['qualified']);self.assertIn('unavailable',r['originalInputsValidation'])
    def test_missing_case_bool_window_mislabeled_modes_and_fakeapproval_reject(self):
        for mutate in (lambda p:p.update(accepted=True),lambda p:p['sourceInfo'].update(acceptedProtocolSourceSha='f'*40),lambda p:p['protocol'].update(warmups=True),lambda p:p['protocol']['cases'].pop(),lambda p:p['protocol']['supplementalCoverage'][0]['origin'].update(smoke=False),lambda p:p['protocol']['cases'][0].pop('mixedEvidence'),lambda p:p['protocol']['cases'][0].update(preflight={}),lambda p:p.update(validation=dict(status='accepted'))):
            p=copy.deepcopy(self.valid);mutate(p)
            with self.subTest(mutation=mutate),self.assertRaises((ValueError,KeyError)):validate_complete_schema(p)
    def test_actual_profile_and_pose_shapes_cannot_be_empty_or_boolean_coordinates(self):
        for mutate in (lambda c:c.update(effectiveProfile={}),lambda c:c['effectiveProfile'].update(atmosphere='Medium'),lambda c:c['effectiveProfile'].update(textures='Invented'),lambda c:c['effectiveProfile'].update(shadows={'anything':None}),lambda c:c['effectiveProfile']['postProcessing'].update(antialiasing='Fxaa'),lambda c:c['lighting'].update(toneMapping='Invented'),lambda c:c['atmosphere'].update(enabled=True),lambda c:c.update(lighting={}),lambda c:c.update(atmosphere={}),lambda c:c['sampleCameras'].__setitem__(0,None),lambda c:c['initialCamera']['target'].update(x=True),lambda c:c['effectiveProfile']['requestedSettings'].update(quality='Low'),lambda c:c['atmosphere'].update(density=float('nan'))):
            p=copy.deepcopy(self.valid);mutate(p['protocol']['cases'][0])
            with self.assertRaises((ValueError,KeyError)):validate_complete_schema(p)
    def test_wrong_order_missing_duplicate_or_interleaved_schedule_reject(self):
        for mutate in (lambda rows:rows.pop(),lambda rows:rows.__setitem__(1,copy.deepcopy(rows[0])),lambda rows:rows.reverse(),lambda rows:rows.__setitem__(slice(0,2),rows[1::-1])):
            p=copy.deepcopy(self.valid);mutate(p['protocol']['attemptSchedule'])
            with self.assertRaises(ValueError):validate_complete_schema(p)
    def test_nonadjacent_nested_and_windows_alias_paths_reject(self):
        for paths in (('attempts/a','attempts/a-1','attempts/a/z'),('attempts/A','attempts/a','attempts/b'),('attempts/a.','attempts/b','attempts/c'),('attempts/a:stream','attempts/b','attempts/c'),('C:escape','attempts/b','attempts/c')):
            p=copy.deepcopy(self.valid)
            for row,path in zip(p['protocol']['attemptSchedule'],paths):row['directory']=path
            with self.assertRaises(ValueError):validate_complete_schema(p)
    def test_partial_unsafe_refs_and_case_alias_payloads_reject(self):
        for mutate in (lambda p:p['policyReferences']['hardware'].update(path='docs/rendering/render-engine-upgrade/policies/../../escape'),lambda p:p['nativeIdentityEvidence'].update(path='D:escape'),lambda p:p['protocol']['supplementalCoverage'][0].update(originEvidence={}),lambda p:p['sourceInfo']['toolScript'].update(bytesBase64=''),lambda p:p['payloadInventory'].append(dict(path='forgeline.client.dll',bytes=1,sha256='b'*64))):
            p=copy.deepcopy(self.valid);mutate(p)
            with self.assertRaises((ValueError,KeyError)):validate_complete_schema(p)
    def test_drive_relative_ads_and_reserved_inventory_names_reject(self):
        for path in ('C:relative','safe/file:stream','CON.txt','safe/trailing.','safe/trailing ','safe/../escape'):
            with self.subTest(path=path),self.assertRaises(ValueError):payload([dict(path=path,bytes=1,sha256='a'*64)])
    def test_shape_does_not_grant_missing_original_input_or_source_binding(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises((ValueError,OSError)):validate_bound_proposal(self.valid,Path(root),Path(__file__).resolve().parents[3])

class OriginalPreflightBindingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):cls.case=next(c for c in fixture()['protocol']['cases'] if c['runProfile']=='Warm' and c['height']==1080)
    def value(self):
        case=copy.deepcopy(self.case);case.pop('preflight');case.pop('coverage');case.pop('mixedEvidence',None)
        source='a'*40;request=dict(commit=source,scenes=[case['scene']],profiles=[case['quality']],zooms=[case['zoom']],displays=[dict(width=case['width'],height=case['height'])],runProfile=case['runProfile'],cameraPath=case['cameraPath'],uiScenarios=['World'],rasterDpi=96,warmups=64,samples=512,repeatCaptures=8)
        raw=canonical(request);rows=[dict(path='ForgeLine.Client.dll',bytes=1,sha256='a'*64)]
        return dict(schemaVersion=1,kind='actual-fixture-preflight-proposal',accepted=False,qualified=False,sourceSha=source,buildIdentity='0.1.0+'+source,runtimeFingerprint='b'*64,payloadBefore=rows,payloadAfter=copy.deepcopy(rows),clientAssemblySha256='a'*64,requestBytesBase64=base64.b64encode(raw).decode(),requestSha256=sha256_bytes(raw),compositionApplied=False,renderingValidated=False,submittedOrPresentedFrames=0,case=case,origin=dict(kind='authored-reference-fixture',actualGameplayAttestationAvailable=False,scene=case['scene'],snapshotFingerprint=case['recipe'].get('snapshotFingerprint')),capabilityInputs=dict(supportsPostProcessing=True,supportsParticleRendering=True,supportsDirectionalShadows=True,supportsAtmosphere=True,isSoftwareAdapter=False,bufferCount=3,availableMemoryBudgetBytes=None,actualSurface=dict(width=case['width'],height=case['height'],bufferCount=3,frameIndex=0,isSuspended=False,isOccluded=False,resizePending=False,presentMode='Immediate',resizeGeneration=0,appliedResizeGeneration=0,submittedFrameCount=0,presentedFrameCount=0)))
    def check(self,value):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);raw=canonical(value);(root/'preflight.json').write_bytes(raw)
            return preflight(root,dict(path='preflight.json',sha256=sha256_bytes(raw)),'a'*40)
    def change_request(self,value,**changes):
        request=json.loads(base64.b64decode(value['requestBytesBase64']));request.update(changes);raw=canonical(request);value.update(requestBytesBase64=base64.b64encode(raw).decode(),requestSha256=sha256_bytes(raw))
    def test_original_producer_shaped_explicit96_input_binds_without_rendering_claim(self):
        value,case=self.check(self.value());self.assertFalse(value['renderingValidated']);self.assertEqual(case['rasterDpi'],96)
    def test_effective96_does_not_authorize_original_default0_or_intrusive_flags(self):
        for change in (dict(rasterDpi=0),dict(captureStageDiagnostics=True),dict(captureShadowDiagnostics=True),dict(continueAfterMismatch=True),dict(warmups=True),dict(ownerTraceControlDirectory='trace')):
            value=self.value();self.change_request(value,**change)
            with self.assertRaises(ValueError):self.check(value)
    def test_actual_capability_surface_types_and_raster_cannot_diverge(self):
        for mutate in (lambda c:c.update(bufferCount=4),lambda c:c.update(bufferCount=True),lambda c:c.update(availableMemoryBudgetBytes=True),lambda c:c.update(availableMemoryBudgetBytes=-1),lambda c:c['actualSurface'].update(width=176),lambda c:c['actualSurface'].update(bufferCount=4),lambda c:c['actualSurface'].update(frameIndex=True),lambda c:c['actualSurface'].update(submittedFrameCount=1)):
            value=self.value();mutate(value['capabilityInputs'])
            with self.assertRaises(ValueError):self.check(value)
        for budget in (None,0,2**64-1):
            value=self.value();value['capabilityInputs']['availableMemoryBudgetBytes']=budget;self.check(value)
    def test_independently_typed_after_inventory_rejects_boolean_integer_alias(self):
        value=self.value();value['payloadAfter'][0]['bytes']=True
        with self.assertRaises(ValueError):self.check(value)
    def test_cold_requires_original_request64_even_when_effective_warmups_zero(self):
        value=self.value();case=value['case'];case.update(runProfile='RendererCold',warmups=0,samples=128,sampleCameras=case['sampleCameras'][:128]);case['recipe']['samples']=128;raw=canonical(case['recipe']);case.update(recipeInputBytesBase64=base64.b64encode(raw).decode(),recipeFingerprint=sha256_bytes(raw));self.change_request(value,runProfile='RendererCold',samples=128,warmups=64)
        self.check(value);self.change_request(value,warmups=0)
        with self.assertRaises(ValueError):self.check(value)

class ProposalCliRetentionTests(unittest.TestCase):
    def test_failed_assembly_and_validation_retain_explicit_unaccepted_attempts(self):
        import baseline
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            for mode,argument in (('assemble-proposal','--draft'),('validate-proposal','--input')):
                output=root/(mode+'.json')
                self.assertEqual(baseline.main([mode,argument,str(root/'missing.json'),'--repository',str(root),'--output',str(output)]),2)
                value=json.loads(output.read_bytes());self.assertEqual(value['status'],'failed');self.assertIs(value['accepted'],False);self.assertIs(value['qualified'],False)
    def test_existing_failure_output_is_never_replaced(self):
        import baseline
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);output=root/'existing.json';output.write_bytes(b'original attempt')
            self.assertEqual(baseline.main(['validate-proposal','--input',str(root/'missing'),'--repository',str(root),'--output',str(output)]),2)
            self.assertEqual(output.read_bytes(),b'original attempt')
if __name__=='__main__':unittest.main()
