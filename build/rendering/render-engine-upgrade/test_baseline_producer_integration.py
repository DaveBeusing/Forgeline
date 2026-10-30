"""Synthetic contract-only integration, using actual C# serialized shapes; NEVER physical baseline evidence.
Only full-matrix enumeration and external trusted Git/CI acquisition are stubbed here; independent Git/matrix/CI tests cover those boundaries.
"""
import base64,copy,gzip,json,struct,tempfile,unittest,zlib
from datetime import datetime,timedelta,timezone
from pathlib import Path
from unittest.mock import patch
import baseline_admission as a
from adapter_identity import bind_adapter
from baseline_contracts import sha256_bytes,canonical
from provider import FIELDS,parse_inventory

SHAPES=Path(__file__).with_name('testdata')/'producer-shapes.synthetic.json.gz'

def png(width,height,pixels):
    def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
    scan=b''.join(b'\0'+pixels[y*width*4:(y+1)*width*4] for y in range(height))
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(scan))+chunk(b'IEND',b'')

def put(root,name,value,raw=False):
    p=root/name;p.parent.mkdir(parents=True,exist_ok=True);data=value if raw else json.dumps(value,separators=(',',':')).encode();p.write_bytes(data)
    return dict(path=name,sha256=sha256_bytes(data))

class SyntheticProducerCohort:
    def __init__(self,root):
        self.root=Path(root);shape=json.loads(gzip.decompress(SHAPES.read_bytes()));self.shape=shape
        self.source=shape['owner']['sourceSha'];self.runtime='b'*64;self.sdk='source-pinned-sdk'
        self.raw_sensor=b'GPU-1234,00000000:30:00.0,SYNTHETIC CONTRACT GPU,596.53,40,1000,10,0x0000000000000000\n'
        self.origin=shape['owner']['sessionOrigin']
        self.camera=shape['native']['cpuFrames'][0]['actualCamera']
        recipe=dict(kind='IndustrialBase',alpha=1,syntheticContractOnly=True,camera=self.camera,runtimeFingerprint=self.runtime)
        recipe_bytes=json.dumps(recipe).encode();self.case=dict(id='fixture',scene='IndustrialBase',quality='Medium',zoom='NormalGameplay',width=16,height=16,runProfile='Warm',cameraPath='Fixed',rasterDpi=96,uiScenario='World',recipe=recipe,recipeFingerprint=sha256_bytes(recipe_bytes),recipeInputBytesBase64=base64.b64encode(recipe_bytes).decode(),sampleCameras=[self.camera]*512,effectiveProfile=dict(active='Medium'))
        self.policies={}
        for kind,names in (('allocation-budget',('processAllocatedBytes','threadAllocatedBytes')),('gpu-memory-budget',a.MEMORY_COUNTERS)):
            self.policies[kind]=dict(schemaVersion=1,kind=kind,reviewer='SYNTHETIC TEST REVIEW',rationale='Unit test only; never accepted real policy',reviewedAtUtc='2026-10-28T00:00:00Z',maximumPerSample={n:2000000000 for n in names})
        self.policies['gpu-memory-budget']['assumptions']=dict(frameBufferCount=3,maximumExpandedDrawnParts=100000,maximumHudVertices=100000)
        self.policies['hardware-stability-policy']=dict(schemaVersion=1,kind='hardware-stability-policy',reviewer='SYNTHETIC TEST REVIEW',rationale='Unit test only',reviewedAtUtc='2026-10-28T00:00:00Z',maximumTemperatureC=100,minimumClockMHz=1,maximumPowerWatts=500,requireAcPower=True,requireNoActiveClockEventReasons=True)
        self.protocol=dict(schemaVersion=1,kind='accepted-baseline-control-protocol',comparison='identical-instrumented-source-A/A',executionSource=self.source,frozenAtUtc='2026-10-29T00:00:00Z',pairs=5,regressionPercent=15,preserveAllAttempts=True,attemptInventoryRoot='attempts',payloadInventory=[dict(path='client.dll',bytes=1,sha256='c'*64)],supplementalCoverage=[dict(id='game',mode='actual-live-gameplay',origin=self.origin['inputs'],originFingerprint=self.origin['fingerprint']),dict(id='collector',mode='observer-on-off',fixtureCaseId='fixture')],cases=[self.case])
        self.records={};self.cohort=dict(schemaVersion=1,kind='baseline-control-cohort',sourceSha=self.source,attemptInventoryRoot='attempts',protocol=dict(path=a.ROOT+'protocol.json',sha256='d'*64),policies={k:dict(path=a.ROOT+k+'.json',sha256='e'*64) for k in self.policies},requiredCi=dict(path='ci.json',sha256='f'*64),runs=[])
        self.schedule=[]
        for pair in range(1,6):
            for arm in (('A','B') if pair%2 else ('B','A')):
                for case_id,producer in (('fixture','native'),('fixture','host'),('game','gameplay'),('collector','collector-on' if arm=='A' else 'collector-off')):
                    self.add_run(pair,arm,case_id,producer,len(self.schedule))
        self.protocol['attemptSchedule']=self.schedule
        sample_native=next(self.records[r['directory']]['manifest'] for r in self.cohort['runs'] if r['producer']=='native')
        self.protocol['nativeHardwareIdentitySha256']=a.native_identity(sample_native,self.sdk)['hardwareIdentitySha256']
    def stamp(self,index,seconds):return (datetime(2026,10,31,tzinfo=timezone.utc)+timedelta(minutes=index,seconds=seconds)).isoformat()
    def owner(self,index):
        owner=copy.deepcopy(self.shape['owner']);owner.update(sourceSha=self.source,runtimeFingerprint=self.runtime)
        base=copy.deepcopy(owner['frames'][0]);base_tick=base['startTick'];step=max(10000,base['serviceEndTick']-base['serviceStartTick']+100)
        frames=[];gpu=[]
        for i in range(512):
            f=copy.deepcopy(base);offset=i*step
            for k in ('startTick','endTick','serviceStartTick','serviceEndTick'):f[k]+=offset
            for interval in [f['signalWait'],*f['observer'].values(),*f['phases'].values()]:
                for k in ('startTick','endTick'):interval[k]+=offset
            f.update(index=i,submission=i+65,submittedFence=i+1000,generation=2,resourceGeneration=1)
            f['simulation'].update(snapshotTick=i+100,completedTicks=i+100)
            g=copy.deepcopy(self.shape['rawGpuTiming']['completions'][0]);g.update(submittedFence=i+1000,completedFence=i+1000)
            for k in ('startTick','endTick','worldEndTick','overlayStartTick'):g[k]+=i*10
            f['gpu']={k:g[k] for k in ('queue','generation','resourceGeneration','submittedFence','completedFence','frequencyHz','startTick','endTick')}
            frames.append(f);gpu.append(g)
        owner.update(frames=frames,gpuCompletions=gpu,producerMode='actual-live-gameplay',modeAttestationAvailable=True)
        owner['window'].update(samples=512,warmups=64);owner['ownerThread']['processId']=42
        owner['clockAnchor'].update(beforeTick=base_tick,afterTick=base_tick,utc=self.stamp(index,10))
        return owner
    def add_run(self,pair,arm,case_id,producer,index):
        relative=f'attempts/{index:03d}';root=self.root/relative;root.mkdir(parents=True)
        identity=dict(schemaVersion=1,adapterLuidHex='0000000000015520',nodeCount=1,queueNodeMask=0,effectiveNodeMask=1,generation=2,processId=42,processStartedFileTimeUtc=123,sessionId='a'*32)
        req=dict(outputRoot='SYNTHETIC/'+relative+'/capture',commit=self.source,runtimeRoot='SYNTHETIC-RUNTIME',warmups=64,samples=512,runProfile='Warm',cameraPath='Fixed',scenes=['IndustrialBase'],profiles=['Medium'],zooms=['NormalGameplay'],displays=[dict(width=16,height=16)],rasterDpi=96,collectorMode='CollectorOn' if producer=='collector-on' else 'CollectorOff' if producer=='collector-off' else 'Full')
        r=dict(pair=pair,arm=arm,caseId=case_id,producer=producer,directory=relative)
        self.schedule.append(dict(pair=pair,arm=arm,caseId=case_id,producer=producer,directory=relative,launchOutputPath=req['outputRoot']))
        r['request']=put(root,'actual-request.json',req)
        argv=['SYNTHETIC/client.exe','--smoke-test'] if producer=='gameplay' else ['SYNTHETIC/client.exe','--reference-host-fixture-request' if producer!='native' else '--reference-capture-request',str(root/'actual-request.json')]
        checkpoint=dict(kind='actual-renderer-identity-checkpoint',runId='b'*32,sourceSha=self.source,runtimeFingerprint=self.runtime,clientAssemblySha256='c'*64,createdAtUtc=self.stamp(index,1),adapterIdentity=identity,nativeWindowHandle=123)
        r['checkpoint']=put(root,'identity/checkpoint.json',checkpoint)
        cuda=dict(kind='cuda-physical-device-inventory',schemaVersion=1,status='available',dllSha256Before='a'*64,dllSha256After='a'*64,startedAtUtc=self.stamp(index,2),completedAtUtc=self.stamp(index,3),calls=[dict(function=n,result=0) for n in ('cuInit','cuDriverGetVersion','cuDeviceGetCount','cuDeviceGet','cuDeviceGetLuid','cuDeviceGetUuid_v2','cuDeviceGetPCIBusId')],devices=[dict(adapterLuidHex=identity['adapterLuidHex'],nodeMask=1,luidBytesHex='2055010000000000',uuid='GPU-1234',pciBusId='0000:30:00.0')])
        r['cudaInventory']=put(root,'identity/cuda-inventory.json',cuda)
        r['bindingSensorRaw']=put(root,'identity/binding-sensor-inventory.stdout',self.raw_sensor,True)
        query=['SYNTHETIC/sensor.exe','--query-gpu='+FIELDS,'--format=csv,noheader,nounits']
        def command(name,argv,raw):
            out=put(root,'identity/'+name+'.stdout',raw,True);err=put(root,'identity/'+name+'.stderr',b'',True)
            return put(root,'identity/'+name+'.command.json',dict(argv=argv,exitCode=0,failure=None,outputs=dict(stdout=dict(path=Path(out['path']).name,sha256=out['sha256']),stderr=dict(path=Path(err['path']).name,sha256=err['sha256']))))
        r['bindingSensorCommand']=command('sensor-query',query,self.raw_sensor)
        version=b'SYNTHETIC provider version';r['bindingProviderVersionCommand']=command('provider-version',['SYNTHETIC/sensor.exe','--version'],version)
        before=dict(alive=True,processId=42,creationFileTimeUtc=123,imageSha256='d'*64,observedAtUtc=self.stamp(index,1.5));after=dict(before,observedAtUtc=self.stamp(index,4))
        context=dict(processId=42,sourceSha=self.source,runtimeFingerprint=self.runtime,clientAssemblySha256='c'*64,startedAtUtc=self.stamp(index,0),argv=argv,requestSha256=r['request']['sha256'])
        binding=bind_adapter((root/r['checkpoint']['path']).read_bytes(),cuda,parse_inventory(self.raw_sensor),before,after,context)
        binding.update(sensorExecutableSha256Before='e'*64,sensorExecutableSha256After='e'*64,providerVersionSha256=sha256_bytes(version))
        r['binding']=put(root,'identity/binding.json',binding)
        owner=self.owner(index);owner.update(adapterIdentity=identity,adapterBinding=dict(binding=binding))
        seal=dict(integrityVersion=1,allRecordSizesAndHashesVerified=True,assetCount=1,manifestSha256='1'*64,contentSignature='2'*64)
        owner['runtimeSeal']=seal
        pixels=b'\x01\x02\x03\xff'*16*16;image=png(16,16,pixels);put(root,'capture/fixed.png',image,True)
        readback=dict(image='fixed.png',imageSha256=sha256_bytes(image),pixelSha256=sha256_bytes(pixels),repeatPixelsEqual=True,records=[dict(pixelSha256=sha256_bytes(pixels)) for _ in range(8)],adapterIdentity=identity)
        acks=[]
        for i,f in enumerate(owner['frames']):acks.append(dict(index=i,ordinal=64+i,generation=2,resourceGeneration=1,submittedFence=1000+i,camera=self.camera,width=16,height=16,bufferCount=3,work=dict(drawnParts=1),hudVertices=0))
        recipe=dict(recipeInputs=self.case['recipe'],recipeInputBytesBase64=self.case['recipeInputBytesBase64'],recipeFingerprint=self.case['recipeFingerprint'])
        fixture=dict(recipe,profile=self.case['effectiveProfile'],frames=acks,readback=readback,adapterBinding=dict(binding=binding),device=owner['actualDevice'])
        if producer=='native':
            native_cpu=[]
            for i,f in enumerate(owner['frames']):
                template=copy.deepcopy(self.shape['native']['cpuFrames'][0]);start=f['startTick'];end=f['endTick']
                template.update(index=i,milliseconds=(end-start)*1000/owner['frequencyHz'],measuredInterval=dict(startTick=start,endTick=end),actualCamera=self.camera,actualSubmission=dict(submission=65+i,submitted=True,generation=2,resourceGeneration=1,submittedFence=1000+i,renderFrame=f['phases']['renderFrame'],present=f['phases']['present'],fenceWait=f['phases']['fenceWait']))
                native_cpu.append(template)
            capture=dict(recipe,profile=self.case['effectiveProfile'],repeatPixelsEqual=True,repeatCaptures=8,image='fixed.png',imageSha256=readback['imageSha256'],pixelSha256=readback['pixelSha256'],captureInputs=readback['records'],run=dict(warmupFrames=64),performance=dict(cpuFrames=native_cpu,cpuTimestampFrequencyHz=owner['frequencyHz'],clockAnchor=owner['clockAnchor'],rawGpuTiming=dict(overflowed=False,scope='native-reference-direct-queue',worldIntervalIncludesShadow=True,completions=owner['gpuCompletions'])),graphics=dict(device=owner['actualDevice'],debug=dict(enabled=False)))
            manifest=dict(status='structural-pass',commit=self.source,buildIdentity='synthetic+'+self.source,runtimeFingerprint=self.runtime,runtimeSeal=seal,adapterIdentity=identity,adapterBinding=dict(binding=binding),captures=[capture],environment=dict(os='SYNTHETIC',architecture='X64'),runtime='SYNTHETIC CLR',driver='596.53')
        elif producer=='gameplay':manifest=owner
        else:
            if producer.startswith('collector-'):
                common=[];step=10000;base=owner['frames'][0]['startTick']-64*step
                for ordinal in range(576):
                    i=ordinal-64
                    if i>=0:start,end=owner['frames'][i]['serviceStartTick'],owner['frames'][i]['serviceEndTick']
                    else:start,end=base+ordinal*step,base+ordinal*step+1
                    common.append(dict(index=i,ordinal=ordinal,startTick=start,endTick=end,submission=dict(submission=ordinal+1,generation=2,resourceGeneration=1,submittedFence=936+ordinal),camera=self.camera,width=16,height=16))
                fixture['collectorControl']=dict(schemaVersion=1,kind='actual-host-collector-control',producerMode='observer-on-off',modeAttestationAvailable=True,collectorEnabled=producer=='collector-on',preciseProcessCountersEnabled=producer=='collector-on',optionalMemoryAndCompletionCollectorEnabled=producer=='collector-on',warmups=64,samples=512,frames=common,clockAnchor=owner['clockAnchor'],frequencyHz=owner['frequencyHz'])
            manifest=dict(status='structural-pass',sourceSha=self.source,runtimeFingerprint=self.runtime,runtimeSeal=seal,fixture=fixture)
            if producer!='collector-off':
                r['owner']=put(root,'capture/owner.json',owner);manifest.update(owner='owner.json',ownerSha256=r['owner']['sha256'])
            else:manifest.update(owner=None,ownerSha256=None)
        r['manifest']=put(root,'capture/manifest.json',manifest)
        for key,name in (('payloadInventoryBefore','payload-before.json'),('payloadInventoryAfter','payload-after.json')):r[key]=put(root,name,self.protocol['payloadInventory'])
        r['preWindowControl']=put(root,'pre-window.json',dict(status='controlled',ownerPid=42,requestedPid=42,hwnd=123,foregroundHwnd=123,foregroundOwnerPid=42,visible=True,iconic=False,candidates=[123],reasons=[],observedAtUtc=self.stamp(index,4)))
        rows=[]
        for i in range(21):
            sample=dict(observedAtUtc=self.stamp(index,i),acquisitionStartedAtUtc=self.stamp(index,i),devices=parse_inventory(self.raw_sensor),power=dict(powerSource='ac',raw=dict(ACLineStatus=1)),window=dict(status='controlled',ownerPid=42,requestedPid=42,hwnd=123,foregroundHwnd=123,foregroundOwnerPid=42,visible=True,iconic=False,candidates=[123],reasons=[]),command=dict(argv=query,exitCode=0,timedOut=False))
            rows.append(dict(index=i,sample=sample,rawStdoutBase64=base64.b64encode(self.raw_sensor).decode(),rawStderrBase64=''))
        journal=b''.join(json.dumps(row,separators=(',',':')).encode()+b'\n' for row in rows)
        put(root,'sensors/observations.jsonl',journal,True);put(root,'sensors/final.json',dict(rows=21,bytes=len(journal),sha256=sha256_bytes(journal),failure=None))
        attempt=dict(status='structural-pass',exitCode=0,failures=[],sourceSha=self.source,commandStartedAtUtc=self.stamp(index,0),commandCompletedAtUtc=self.stamp(index,20),requestSha256=r['request']['sha256'],runtimeFingerprint=self.runtime,clientExecutableSha256='d'*64,clientAssemblySha256='c'*64,sensorExecutableSha256='e'*64,providerVersionSha256=sha256_bytes(version),processId=42,argv=argv,captureMode='actual-live-gameplay' if producer=='gameplay' else 'reference-host-fixture' if producer!='native' else 'native-reference',payloadInventoryBeforeSha256=r['payloadInventoryBefore']['sha256'],payloadInventoryAfterSha256=r['payloadInventoryAfter']['sha256'])
        r['attempt']=put(root,'attempt.json',attempt);self.cohort['runs'].append(r);self.records[relative]=dict(attempt=attempt,manifest=manifest,request=req,binding=binding)
    def evaluate(self):
        def committed(repo,trusted,ref):return self.protocol if ref==self.cohort['protocol'] else self.policies[next(k for k,v in self.cohort['policies'].items() if v==ref)]
        def git(repo,*args):
            return json.dumps(dict(sdk=dict(version=self.sdk))).encode() if args[:1]==('show',) else b'2026-10-29T00:00:00Z' if args[:1]==('log',) else b''
        with patch.object(a,'committed',side_effect=committed),patch.object(a,'git',side_effect=git),patch.object(a,'validate_protocol',return_value={'fixture':self.case}),patch.object(a,'validation_imports',return_value=({'requiredCi':{'status':'pass'}},[])):
            return a.evaluate(self.cohort,self.root,self.root,'a'*40)

class ProducerPathIntegration(unittest.TestCase):
    def test_complete_synthetic_schema_path_and_corrupted_producer_joins(self):
        with tempfile.TemporaryDirectory() as root:
            fixture=SyntheticProducerCohort(root)
            result=fixture.evaluate();self.assertTrue(result['baselineControlsAccepted']);self.assertFalse(result['wave2Ready']) # Synthetic contract exercise only; external trust/matrix acquisition stubbed.
            run=fixture.cohort['runs'][0];path=Path(root)/run['directory']/run['manifest']['path'];original=path.read_bytes();original_ref=copy.deepcopy(run['manifest'])
            for defect in ('index','milliseconds','interval','unsubmitted','camera','pixel'):
                v=json.loads(original);row=v['captures'][0]['performance']['cpuFrames'][0]
                if defect=='index':row['index']=1
                elif defect=='milliseconds':row['milliseconds']+=1
                elif defect=='interval':row['measuredInterval']['endTick']=row['measuredInterval']['startTick']
                elif defect=='unsubmitted':row['actualSubmission']['submitted']=False
                elif defect=='camera':row['actualCamera']={}
                else:v['captures'][0]['pixelSha256']='f'*64
                path.write_text(json.dumps(v));run['manifest']['sha256']=sha256_bytes(path.read_bytes())
                with self.subTest(defect=defect),self.assertRaises((ValueError,KeyError)):fixture.evaluate()
                path.write_bytes(original);run['manifest']=copy.deepcopy(original_ref)
            saved=fixture.cohort['runs'].pop()
            with self.assertRaises(ValueError):fixture.evaluate()
            fixture.cohort['runs'].append(saved)
            fixture.protocol['attemptSchedule'][0],fixture.protocol['attemptSchedule'][1]=fixture.protocol['attemptSchedule'][1],fixture.protocol['attemptSchedule'][0]
            with self.assertRaises(ValueError):fixture.evaluate()
            fixture.protocol['attemptSchedule'][0],fixture.protocol['attemptSchedule'][1]=fixture.protocol['attemptSchedule'][1],fixture.protocol['attemptSchedule'][0]
            schedule=copy.deepcopy(fixture.protocol['attemptSchedule'])
            fixture.protocol['attemptSchedule'][0],fixture.protocol['attemptSchedule'][8]=fixture.protocol['attemptSchedule'][8],fixture.protocol['attemptSchedule'][0]
            with self.assertRaises(ValueError):fixture.evaluate()
            fixture.protocol['attemptSchedule']=schedule
            fixture.cohort['runs'].append(copy.deepcopy(fixture.cohort['runs'][0]))
            with self.assertRaises(ValueError):fixture.evaluate()
            fixture.cohort['runs'].pop()
            # Mutate retained records consistently (including their content hash), so each
            # rejection exercises cross-producer semantics, not merely a stale top-level hash.
            for defect in ('interrupted','mixedSource','futurePolicy','rawQuery','rawOutput','replayedLifetime','foregroundFacts','throttling','sensorGap','offMislabel'):
                target=next(r for r in fixture.cohort['runs'] if r['producer']==('collector-off' if defect=='offMislabel' else 'native'))
                directory=Path(root)/target['directory'];saved_ref=copy.deepcopy(target)
                saved_files={p:p.read_bytes() for p in directory.rglob('*') if p.is_file()}
                policies=copy.deepcopy(fixture.policies)
                def change(key,mutate):
                    value=json.loads((directory/target[key]['path']).read_bytes());mutate(value)
                    target[key]=put(directory,target[key]['path'],value)
                if defect=='interrupted':change('attempt',lambda v:v.update(status='failed',exitCode=1,failures=['interrupted'],commandCompletedAtUtc=None))
                elif defect=='mixedSource':change('manifest',lambda v:v.update(commit='e'*40))
                elif defect=='futurePolicy':fixture.policies['allocation-budget']['reviewedAtUtc']='2027-01-01T00:00:00Z'
                elif defect=='rawQuery':change('bindingSensorCommand',lambda v:v['argv'].__setitem__(1,'--query-gpu=name'))
                elif defect=='rawOutput':(directory/'identity/sensor-query.stderr').write_bytes(b'changed raw stderr')
                elif defect=='replayedLifetime':change('binding',lambda v:v['processBefore'].update(creationFileTimeUtc=999))
                elif defect=='offMislabel':change('manifest',lambda v:v['fixture']['collectorControl'].update(collectorEnabled=True))
                else:
                    journal_path=directory/'sensors/observations.jsonl';rows=[json.loads(line) for line in journal_path.read_bytes().splitlines()]
                    if defect=='foregroundFacts':rows[10]['sample']['window']['foregroundHwnd']=456 # Cannot just retain a green status label.
                    elif defect=='sensorGap':rows[10]['sample']['observedAtUtc']=rows[3]['sample']['observedAtUtc']
                    else:
                        bad=fixture.raw_sensor.replace(b'0000000000000000',b'0000000000000004')
                        rows[10]['rawStdoutBase64']=base64.b64encode(bad).decode();rows[10]['sample']['devices']=parse_inventory(bad)
                    raw=b''.join(json.dumps(row,separators=(',',':')).encode()+b'\n' for row in rows);journal_path.write_bytes(raw)
                    put(directory,'sensors/final.json',dict(rows=len(rows),bytes=len(raw),sha256=sha256_bytes(raw),failure=None))
                with self.subTest(defect=defect),self.assertRaises((ValueError,KeyError,TypeError)):fixture.evaluate()
                target.clear();target.update(saved_ref);fixture.policies=policies
                for path,raw in saved_files.items():path.write_bytes(raw)


if __name__=='__main__':unittest.main()
