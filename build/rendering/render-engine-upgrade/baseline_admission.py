"""Baseline-only A/A evidence admission. No future optimization/art/lifecycle gates are inferred."""
from __future__ import annotations
import base64,math,re,subprocess
from datetime import timedelta
from pathlib import Path
from itertools import product
from baseline_contracts import require,parse_json_bytes,sha256_bytes,utc,native_identity
from qualification import MEMORY_COUNTERS,validate_live_owner_telemetry,validate_raw_gpu_timing,distribution,canonical,validation_imports
from adapter_identity import bind_adapter,normalized_pci
from provider import parse_inventory
from sensor_journal import read_journal

ROOT='docs/rendering/render-engine-upgrade/'
SCENES=('IndustrialBase','BattlefieldCombat','CombatSupportRepeated')
SCALED=('Scaling1000High','Scaling1000Low','Scaling10000High','Scaling10000Low','Scaling100000High','Scaling100000Low','ScalingTerrainOnly')
VARIANTS=(('Warm','Fixed'),('Warm','Sweep'),('Warm','Traverse'),('RendererCold','Fixed'))

def integer(v,name,minimum=0):
    require(type(v) is int and v>=minimum,name+' must be an integer within its bound');return v

def digest(v,name,n=64):
    require(type(v) is str and re.fullmatch('[0-9a-fA-F]{'+str(n)+'}',v) is not None,name+' digest missing');return v.lower()

def local(root,value):
    require(type(value) is str and value and not Path(value).is_absolute(),'Evidence paths must be relative')
    root=Path(root).resolve();p=(root/value).resolve();require(p.is_relative_to(root),'Evidence path escapes its retained cohort');return p

def load(root,ref):
    require(type(ref) is dict,'Hash-bound evidence reference required')
    p=local(root,ref.get('path'));raw=p.read_bytes();require(sha256_bytes(raw)==digest(ref.get('sha256'),'evidence'),'Evidence bytes changed: '+str(p));return parse_json_bytes(raw)

def git(repo,*args):
    try:result=subprocess.run(['git',*args],cwd=repo,capture_output=True,timeout=30)
    except subprocess.TimeoutExpired as error:raise ValueError('Git trust check timed out; no admission') from error
    require(result.returncode==0,'Git trust check failed: '+str(args));return result.stdout

def committed(repo,trusted,ref):
    path=ref.get('path');require(type(path) is str and path.startswith(ROOT) and '..' not in Path(path).parts,'Accepted records must be in owned rendering documentation')
    raw=git(repo,'show',trusted+':'+path);require(sha256_bytes(raw)==digest(ref.get('sha256'),'accepted record'),'Record differs from accepted master bytes');return parse_json_bytes(raw)

def required_cases():
    cases=set()
    for scene,profile,zoom,display,variant in product(SCENES,('Medium','High'),('CloseTactical','NormalGameplay','Strategic'),((1920,1080),(2560,1440),(3840,2160)),VARIANTS):
        cases.add((scene,profile,zoom,*display,*variant))
    for scene,path in product(SCALED,('Fixed','Sweep')):cases.add((scene,'High','Strategic',1920,1080,'Warm',path))
    return cases

def signature(case):return (case['scene'],case['quality'],case['zoom'],integer(case['width'],'width',1),integer(case['height'],'height',1),case['runProfile'],case['cameraPath'])

def validate_protocol(protocol):
    require(type(protocol.get('schemaVersion')) is int and protocol['schemaVersion']==1 and protocol.get('kind')=='accepted-baseline-control-protocol','Accepted baseline protocol missing')
    return validate_protocol_body(protocol)

def validate_protocol_body(protocol):
    """Pure case/window semantics; this function does not assert approval or committed trust."""
    require(protocol.get('comparison')=='identical-instrumented-source-A/A' and protocol.get('preserveAllAttempts') is True,'Baseline must preserve every A/A attempt')
    require(type(protocol.get('pairs')) is int and protocol['pairs']>=5,'At least five predeclared pairs required')
    require(type(protocol.get('regressionPercent')) is int and protocol['regressionPercent']==15,'The existing15percent control threshold cannot change')
    require(protocol.get('rasterDpi')==96 and type(protocol['rasterDpi']) is int,'Canonical rasterDpi96 required')
    require(protocol.get('warmups')==64 and protocol.get('warmSamples')==512 and protocol.get('coldSamples')==128,'Frozen64/512 and separate0/128 cold windows required')
    cases=protocol.get('cases');require(type(cases) is list and cases,'Frozen exact case recipes required')
    ids=[c.get('id') for c in cases];require(all(type(v) is str and v for v in ids) and len(set(ids))==len(ids),'Case IDs must be unique')
    actual={signature(c) for c in cases};require(required_cases()<=actual,'Full canonical and named scaled Fixed/Sweep matrix missing')
    coverage={c.get('coverage') for c in cases}
    require('mixed-material-skin-mechanics-effects' in coverage,'Predeclared actual mixed fixture missing')
    require(not coverage.intersection({'actual-live-gameplay','observer-off'}),'Authored reference fixtures cannot be relabelled gameplay or observer-off')
    require(type(protocol.get('supplementalCoverage')) is list,'Separately frozen real gameplay/observer cohorts required')
    for c in cases:
        require(c.get('rasterDpi')==96 and c.get('uiScenario')=='World','Unsupported/caller-renamed UI fixture')
        digest(c.get('recipeFingerprint'),'frozen actual recipe')
        require(type(c.get('sampleCameras')) is list and len(c['sampleCameras'])==(128 if c['runProfile']=='RendererCold' else 512),'Frozen actual camera path missing')
        require(type(c.get('effectiveProfile')) is dict,'Frozen effective profile missing')
        require(type(c.get('recipe')) is dict,'Actual serialized recipe must be frozen before capture')
        raw=base64.b64decode(c.get('recipeInputBytesBase64',''),validate=True)
        require(sha256_bytes(raw)==c['recipeFingerprint'].lower() and parse_json_bytes(raw)==c['recipe'],'Frozen recipe bytes/digest differ')
    return {c['id']:c for c in cases}

def validate_policy(policy,kind,start):
    require(type(policy.get('schemaVersion')) is int and policy['schemaVersion']==1 and policy.get('kind')==kind,'Wrong accepted policy contract')
    require(type(policy.get('reviewer')) is str and policy['reviewer'] and type(policy.get('rationale')) is str and policy['rationale'],'Reviewed policy provenance/rationale required')
    require(utc(policy['reviewedAtUtc'])<=start,'Policy review must predate every capture')
    if kind!='hardware-stability-policy':
        names=('processAllocatedBytes','threadAllocatedBytes') if kind=='allocation-budget' else MEMORY_COUNTERS
        maxima=policy.get('maximumPerSample');require(type(maxima) is dict and set(maxima)==set(names),'All exact measured counter maxima required; physical allocation totals are not invented')
        for name in names:integer(maxima[name],'reviewed '+name)
        if kind=='gpu-memory-budget':
            assumptions=policy.get('assumptions');require(type(assumptions) is dict,'Conditional logical capacity assumptions must be reviewed')
            require(integer(assumptions.get('frameBufferCount'),'fixed frame buffer count',1)==3,'Actual fixed three-buffer contract required')
            integer(assumptions.get('maximumExpandedDrawnParts'),'expanded drawn-part bound',1)
            integer(assumptions.get('maximumHudVertices'),'fixed host HUD vertex bound',1)
    else:
        for name in ('maximumTemperatureC','minimumClockMHz','maximumPowerWatts'):
            require(type(policy.get(name)) in (float,int) and math.isfinite(policy[name]) and policy[name]>0,'Finite previously reviewed hardware bounds required')
        require(policy.get('requireAcPower') is True and policy.get('requireNoActiveClockEventReasons') is True,'AC and conservative no-throttling controls cannot be relaxed')

def validate_recipe(value,case):
    raw=base64.b64decode(value.get('recipeInputBytesBase64',''),validate=True)
    require(sha256_bytes(raw)==case['recipeFingerprint'].lower()==str(value.get('recipeFingerprint','')).lower(),'Actual recipe bytes/digest differ from frozen case')
    require(parse_json_bytes(raw)==value.get('recipeInputs')==case['recipe'],'Actual recipe inputs differ from frozen inputs')

def tuple_of(row):return tuple(integer(row.get(k),k,1) for k in ('generation','resourceGeneration','submittedFence'))

def exact_join(cpu,ack,gpu,warmups):
    require(len(cpu)==len(ack)==len(gpu)>0,'CPU/camera/GPU window counts differ')
    remaining={tuple_of(g) for g in gpu};require(len(remaining)==len(gpu),'Duplicate GPU tuples')
    for i,(c,a) in enumerate(zip(cpu,ack)):
        key=tuple_of(c)
        require(integer(c.get('index'),'CPU index')==i and integer(a.get('index'),'ACK index')==i and integer(a.get('ordinal'),'ACK ordinal')==warmups+i and tuple_of(a)==key and key in remaining,'CPU/ACK/completedGPU one-to-one fence/index/generation differs')
        remaining.remove(key)
    require(not remaining,'Unreferenced completed GPU tuple')

def verify_png(directory,record,case):
    import struct,zlib,hashlib
    path=local(directory,record['image']);require(path.stat().st_size<=64*1024*1024,'PNG exceeds declared image bound')
    raw=path.read_bytes();require(sha256_bytes(raw)==digest(record['imageSha256'],'actual PNG bytes'),'Retained PNG bytes changed')
    require(raw[:8]==b'\x89PNG\r\n\x1a\n','Invalid PNG signature');cursor=8;compressed=bytearray();header=None;ended=False
    while cursor<len(raw):
        require(cursor+12<=len(raw),'Truncated PNG chunk');n=struct.unpack_from('>I',raw,cursor)[0];kind=raw[cursor+4:cursor+8]
        require(cursor+12+n<=len(raw),'PNG chunk escapes file');data=raw[cursor+8:cursor+8+n]
        require(zlib.crc32(kind+data)&0xffffffff==struct.unpack_from('>I',raw,cursor+8+n)[0],'PNG CRC mismatch')
        if kind==b'IHDR':
            require(header is None and n==13,'Invalid repeated PNG header');header=struct.unpack('>IIBBBBB',data)
        elif kind==b'IDAT':compressed.extend(data)
        elif kind==b'IEND':require(n==0,'Invalid PNG end');ended=True;cursor+=12;break
        cursor+=n+12
    require(ended and cursor==len(raw) and header==(case['width'],case['height'],8,6,0,0,0),'PNG format/dimensions differ from actual fixture')
    width,height=case['width'],case['height'];stride=width*4;expected=height*(stride+1)
    dec=zlib.decompressobj();filtered=dec.decompress(compressed,expected+1)
    require(len(filtered)==expected and dec.eof and not dec.unused_data and not dec.unconsumed_tail,'PNG decoded size/stream invalid')
    previous=bytearray(stride);digestor=hashlib.sha256()
    def paeth(a,b,c):
        q=a+b-c;da,db,dc=abs(q-a),abs(q-b),abs(q-c)
        return a if da<=db and da<=dc else b if db<=dc else c
    for y in range(height):
        start=y*(stride+1);mode=filtered[start];require(0<=mode<=4,'Unsupported PNG filter');row=bytearray(filtered[start+1:start+1+stride])
        for x in range(stride):
            a=row[x-4] if x>=4 else 0;b=previous[x];c=previous[x-4] if x>=4 else 0
            row[x]=(row[x]+(0 if mode==0 else a if mode==1 else b if mode==2 else (a+b)//2 if mode==3 else paeth(a,b,c)))&255
        digestor.update(row);previous=row
    require(digestor.hexdigest()==digest(record['pixelSha256'],'actual decoded RGBA bytes'),'Claimed pixel hash differs from retained decoded image')

def verify_command(directory,ref,argv):
    record=load(directory,ref);require(record.get('argv')==argv and record.get('exitCode')==0 and record.get('failure') is None,'Raw query command failed/differs')
    command_directory=local(directory,ref['path']).parent
    for output in record['outputs'].values():
        require(sha256_bytes(local(command_directory,output['path']).read_bytes())==digest(output['sha256'],'query raw output'),'Raw query output changed')
    return record

def validate_native_samples(perf,count):
    cpu=perf['cpuFrames'];frequency=integer(perf['cpuTimestampFrequencyHz'],'original native timestamp frequency',1)
    require(len(cpu)==count,'Native sample count differs')
    previous=-1;previous_submission=0
    for i,f in enumerate(cpu):
        require(type(f.get('index')) is int and f['index']==i,'Original native sample sequence differs')
        interval=f['measuredInterval'];start,end=integer(interval.get('startTick'),'native measured start'),integer(interval.get('endTick'),'native measured end')
        require(previous<=start<end,'Native measured intervals overlap/regress/are empty');previous=end
        ms=f['milliseconds'];require(type(ms) in (int,float) and math.isfinite(ms) and ms>0,'Native wall duration unavailable')
        # .NET GetElapsedTime truncates to one100ns TimeSpan tick; this is representation precision, not a performance tolerance.
        require(abs(ms-(end-start)*1000/frequency)<=0.000100001,'Native milliseconds differ from original measured ticks')
        actual=f['actualSubmission'];require(actual.get('submitted') is True,'Native sample did not submit/present')
        submission=integer(actual.get('submission'),'native submission ordinal',1)
        require(previous_submission==0 or submission==previous_submission+1,'Native submission ordinal skipped/duplicated');previous_submission=submission
        for name in ('renderFrame','present','fenceWait'):
            part=actual[name];a,b=integer(part.get('startTick'),name+' start'),integer(part.get('endTick'),name+' end')
            require(start<=a<=b<=end,'Actual native submission interval outside measured wall bracket')
    return cpu

def sample_times(clock,frames,frequency):
    integer(frequency,'actual timestamp frequency',1);require(clock.get('frequencyHz')==frequency,'Original clock frequency mismatch')
    a,b=integer(clock.get('beforeTick'),'anchor before'),integer(clock.get('afterTick'),'anchor after');require(a<=b,'Invalid original clock bracket')
    origin=utc(clock['utc']);return (origin+timedelta(seconds=(frames[0]['startTick']-b)/frequency),origin+timedelta(seconds=(frames[-1]['endTick']-a)/frequency))

def actual_foreground(facts,pid,hwnd):
    require(facts.get('status')=='controlled' and type(facts.get('requestedPid')) is int and facts['requestedPid']==pid and
        type(facts.get('ownerPid')) is int and facts['ownerPid']==pid and type(facts.get('hwnd')) is int and facts['hwnd']==hwnd and
        facts.get('foregroundHwnd')==hwnd and facts.get('foregroundOwnerPid')==pid and facts.get('visible') is True and facts.get('iconic') is False and
        type(facts.get('candidates')) is list and hwnd in facts['candidates'] and not facts.get('reasons'),
        'Actual retained Win32 facts do not prove the exact producer HWND/PID was visible, unminimized and foreground')

def validate_controls(rows,start,end,binding,policy,before_control=None,hwnd=None):
    require(rows and utc(rows[0]['sample']['acquisitionStartedAtUtc'])<=start and utc(rows[-1]['sample']['observedAtUtc'])>=end,'Physical journal does not cover the original measured window')
    controlled=[]
    if before_control is not None:
        actual_foreground(before_control,binding['rendererIdentity']['processId'],hwnd)
        controlled.append(utc(before_control['observedAtUtc']))
    for row in rows:
        s=row['sample'];at=utc(s['observedAtUtc'])
        if start-timedelta(seconds=5)<=at<=end+timedelta(seconds=5):
            require(s.get('failure') is None and type(s['power'].get('raw',{}).get('ACLineStatus')) is int and s['power']['raw']['ACLineStatus']==1 and s['power']['powerSource']=='ac','Actual raw OS AC/sensor control failed')
            matches=[d for d in s['devices'] if d['uuid']==binding['uuid'] and normalized_pci(d['pciBusId'])==binding['pciBusId']]
            require(len(matches)==1,'Bound physical device absent/ambiguous in sensor sample');d=matches[0]
            require(d['temperatureC']<=policy['maximumTemperatureC'] and d['clockMHz']>=policy['minimumClockMHz'] and d['powerWatts']<=policy['maximumPowerWatts'],'Previously reviewed hardware stability range exceeded')
            require(d['eventReasons']['mask']==0 and d['eventReasons']['unknownBits']==0,'Active/unknown clock-event reasons invalidate physical stability')
            w=s['window']
            if w.get('status')=='controlled':
                actual_foreground(w,binding['rendererIdentity']['processId'],hwnd);controlled.append(at)
            elif start<=at<=end:require(False,'Foreground lost/unavailable within measured window')
    require(controlled and controlled[0]<=start and controlled[-1]>=end,'Actual foreground point observations do not bracket the complete measured window')

def stable_pair(a,b):
    require(type(a) in (int,float) and type(b) in (int,float) and math.isfinite(a) and math.isfinite(b) and a>=0 and b>=0,'Invalid A/A metric')
    if a==b==0:return
    from decimal import Decimal
    require(min(a,b)>0 and Decimal(str(max(a,b)))*100<=Decimal(str(min(a,b)))*115,'A/A control exceeds unchanged15percent in either direction')

def validate_gameplay(owner,frozen):
    validate_live_owner_telemetry(owner)
    require(owner.get('producerMode')=='actual-live-gameplay' and owner.get('modeAttestationAvailable') is True,'Reference fixtures/context labels cannot attest actual gameplay')
    origin=owner['sessionOrigin'];raw=base64.b64decode(origin['inputBytesBase64'],validate=True)
    require(sha256_bytes(raw)==digest(origin['fingerprint'],'actual session origin')==digest(frozen['originFingerprint'],'frozen session origin') and parse_json_bytes(raw)==origin['inputs']==frozen['origin'],'Actual scenario/seed/settings/load/smoke origin differs')
    require(origin['inputs'].get('kind')=='NewGame','Loaded save admission unavailable without authoritative loaded-byte identity; current-file hash cannot attest loaded state')
    require(owner.get('serviceIntervalsAvailable') is True,'Gameplay must retain observer-inclusive service scope')
    frames=owner['frames'];require(len(frames)==512 and owner['window']['warmups']==64,'Gameplay needs64/512 original samples')
    prior_ticks=-1;prior_completed=-1;session=None
    for f in frames:
        proof=f['simulation'];expected=integer(proof.get('expectedSession'),'actual expected session',1)
        session=expected if session is None else session
        require(proof.get('running') is True and session==expected==proof.get('snapshotSession')==proof.get('callbackSession'),'Actual running callback/consumed snapshot sessions differ')
        tick=integer(proof.get('snapshotTick'),'actual snapshot tick');completed=integer(proof.get('completedTicks'),'actual completed ticks')
        require(prior_ticks<=tick and prior_completed<=completed,'Actual simulation/consumed snapshots regress')
        prior_ticks=tick;prior_completed=completed
    require(frames[0]['simulation']['snapshotTick']<prior_ticks and frames[0]['simulation']['completedTicks']<prior_completed,'Actual simulation and consumed snapshots did not progress')
    return frames

def validate_collector_probe(probe,fixture,case,enabled):
    require(type(probe.get('schemaVersion')) is int and probe['schemaVersion']==1 and probe.get('kind')=='actual-host-collector-control' and probe.get('producerMode')=='observer-on-off' and probe.get('modeAttestationAvailable') is True,'Actual supplemental common probe missing')
    require(probe.get('collectorEnabled') is enabled and probe.get('preciseProcessCountersEnabled') is enabled and probe.get('optionalMemoryAndCompletionCollectorEnabled') is enabled,'Actual optional collector mode differs')
    require(probe.get('warmups')==64 and probe.get('samples')==512,'Collector experiment needs equal64/512 windows')
    frames=probe['frames'];require(len(frames)==576 and len(fixture['frames'])==512,'Every common-probe warmup and measured frame must be retained')
    previous=-1;last_fence=0
    for ordinal,f in enumerate(frames):
        require(type(f.get('ordinal')) is int and type(f.get('index')) is int and f['ordinal']==ordinal and f['index']==ordinal-64,'Common probe ordinal/index is missing/repeated')
        a,b=integer(f['startTick'],'common start'),integer(f['endTick'],'common end');require(previous<=a<b,'Common probe chronology invalid');previous=b
        actual=f['submission'];fence=integer(actual.get('submittedFence'),'actual common fence',1);require(last_fence<fence,'Common probe fence duplicated/regressed');last_fence=fence
        if ordinal>=64:
            ack=fixture['frames'][ordinal-64]
            require(tuple_of(actual)==tuple_of(ack) and ack['ordinal']==ordinal and ack['index']==ordinal-64 and f['camera']==ack['camera']==case['sampleCameras'][ordinal-64] and f['width']==case['width'] and f['height']==case['height'],'Common probe/actual camera ACK mismatch')
    return frames[64:]

def validate_mixed_consumption(rows,case,producer,cpu_frames=None):
    require(case['recipe'].get('kind')==case['scene'] and case['scene']!='ScalingTerrainOnly','Mixed coverage must bind a reviewed concrete content recipe')
    consumed=False
    previous_epoch=0
    for index,row in enumerate(rows):
        content=row.get('content') if producer=='host' else row.get('submission',{}).get('content')
        particles=row.get('particles') if producer=='host' else row.get('submission',{}).get('particles')
        require(type(content) is dict and type(particles) is dict,'Actual same-frame consumed main content/particles unavailable')
        actual=row if producer=='host' else row['actualSubmission']
        require(tuple_of(content['actualSubmission'])==tuple_of(actual),'Consumed content stamp belongs to a stale/other successful frame')
        epoch=integer(content.get('recordingEpoch'),'actual recording epoch',1);require(previous_epoch<epoch,'Consumed main recording epoch is stale/reused');previous_epoch=epoch
        require(0<=integer(content.get('frameIndex'),'recorded frame slot')<3,'Invalid fixed3-buffer recording slot')
        interval=cpu_frames[index]['phases']['renderFrame'] if producer=='host' else row['actualSubmission']['renderFrame']
        require(interval['startTick']<=integer(content.get('observerStartTick'),'content observer start')<=integer(content.get('observerEndTick'),'content observer end')<=interval['endTick'],'Consumed-content observation lies outside the actual recorded frame')
        for name in ('distinctRuntimeMaterials','skinnedInstances','mechanicalInstances','mainInstances','fallbackInstances'):integer(content.get(name),'actual '+name)
        if content['distinctRuntimeMaterials']>=2 and content['skinnedInstances']>0 and content['mechanicalInstances']>0 and content['fallbackInstances']==0 and particles.get('activeParticles',0)>0 and particles.get('drawCalls',0)>0:
            consumed=True
    require(consumed,'No measured frame actually consumed distinct runtime materials, skin, mechanical parts AND active drawn effects together; labels/loaded caches cannot qualify')

def measured_distributions(producer,cpu,gpu,frequency,*,owner=None,setup=None):
    phases={}
    if producer in ('host','gameplay'):
        for name in ('host','prepare','renderFrame','present','fenceWait'):
            phases[name]=distribution([(f['phases'][name]['endTick']-f['phases'][name]['startTick'])*1000/frequency for f in cpu])
    elif producer=='native':
        for name in ('renderFrame','present','fenceWait'):
            phases[name]=distribution([(f['actualSubmission'][name]['endTick']-f['actualSubmission'][name]['startTick'])*1000/frequency for f in cpu])
    passes={}
    for name,start,end in (('worldIncludingShadow','startTick','worldEndTick'),('overlay','overlayStartTick','endTick'),('shadow','shadowStartTick','shadowEndTick'),('particles','particleStartTick','particleEndTick')):
        passes[name]=distribution([(g[end]-g[start])*1000/g['frequencyHz'] for g in gpu if g.get(start) is not None and g.get(end) is not None])
    allocations={name:distribution([f[name] for f in cpu]) if producer in ('host','gameplay') else distribution([]) for name in ('processAllocatedBytes','threadAllocatedBytes')}
    memory={name:distribution([f['memory'][name] for f in cpu]) if producer in ('host','gameplay') else distribution([]) for name in MEMORY_COUNTERS}
    return dict(phases=phases,gpuPasses=passes,fullFrameAllocations=allocations,logicalMemory=memory,setup=setup,
        scope='Inclusive/overlapping intervals are not summed; cold setup remains separate; unavailable process/thread/physical/stack data are not zero',
        sampledAllocationSites='Separate traced producer evidence required; these exact totals contain no stacks')

def verify_trace_execution(root,wrapper,report,source,actual_trace):
    provenance=load(root,wrapper['executionProvenance'])
    require(provenance.get('sourceSha')==source and provenance.get('sourceTreeClean') is True,'Trace parser execution lacks exact clean source provenance')
    require(provenance.get('inputTraceSha256')==actual_trace and provenance.get('outputReportSha256')==wrapper['report']['sha256'],'Executed parser input/output hashes do not bind this trace/report')
    require(str(report.get('parserBuildIdentity','')).lower().endswith(source),'Actual parser build identity differs from admitted source')
    def inventory(before_key,after_key):
        before=load(root,provenance[before_key]);after=load(root,provenance[after_key]);require(before==after and type(before) is list and before,'Trace tool/parser payload changed or missing')
        paths={r['path']:r for r in before};require(len(paths)==len(before),'Duplicate executable payload identity')
        for row in before:integer(row.get('bytes'),'trace payload bytes');digest(row.get('sha256'),'trace payload')
        return paths
    tools=inventory('toolInventoryBefore','toolInventoryAfter');parsers=inventory('parserInventoryBefore','parserInventoryAfter')
    require(any(Path(path).name.lower()=='dotnet-trace.dll' for path in tools),'Actual pinned managed dotnet-trace payload missing')
    tool=provenance['toolExecutable'];parser=provenance['parserAssembly']
    require(Path(tool).is_absolute() and Path(parser).is_absolute(),'Actual trace/parser executable paths must be absolute')
    require(provenance['toolExecutableInventoryPath'] in tools and Path(provenance['toolExecutableInventoryPath']).name.lower()==Path(tool).name.lower(),'Actual tool apphost absent from retained inventory')
    require(provenance['parserAssemblyInventoryPath'] in parsers and parsers[provenance['parserAssemblyInventoryPath']]['sha256']==report.get('parserAssemblySha256') and Path(provenance['parserAssemblyInventoryPath']).name==Path(parser).name,'Parsed report belongs to another parser assembly payload')
    version=verify_command(root,provenance['versionCommand'],[tool,'--version'])
    version_path=local(root,provenance['versionCommand']['path']).parent/version['outputs']['stdout']['path']
    require(version_path.read_text(encoding='utf-8-sig').strip().startswith('10.0.750501'),'Actual pinned dotnet-trace version unavailable/mismatched')
    pid=integer(report.get('expectedPid'),'traced PID',1)
    collect=load(root,wrapper['command'])
    duration=provenance['duration'];require(type(duration) is str and re.fullmatch(r'00:00:(?:0[1-9]|[12][0-9]|30)',duration),'Sampled attach duration must be explicitly bounded1..30seconds')
    expected=[tool,'collect','--process-id',str(pid),'--profile','gc-verbose','--duration',duration,'--buffersize','256','--output',provenance['originalTraceOutputPath']]
    verify_command(root,wrapper['command'],expected)
    parsed=verify_command(root,provenance['parserCommand'],['dotnet',parser,provenance['originalTraceOutputPath'],provenance['originalParserOutputDirectory'],str(pid)])
    require(utc(collect['completedAtUtc'])<=utc(parsed['startedAtUtc'])<=utc(parsed['completedAtUtc']),'Parser ran before original trace completion')
    deps=load(root,provenance['parserDependencies'])
    require('Microsoft.Diagnostics.Tracing.TraceEvent/4.0.0' in deps.get('libraries',{}),'Actual parser dependency payload is not pinned TraceEvent4.0.0')
    deps_path=provenance['parserDependenciesInventoryPath'];require(deps_path in parsers and parsers[deps_path]['sha256']==provenance['parserDependencies']['sha256'],'Parser dependency bytes differ from executed payload inventory')
    from allocation_diagnostics import _VerifiedTraceExecution
    return _VerifiedTraceExecution(sha256_bytes(canonical(report)),wrapper['report']['sha256'],actual_trace,source,provenance['parserDependencies']['sha256'],wrapper['executionProvenance']['sha256'])

def validate_sampled_owner_diagnostic(root,ref,source,runtime,client_image_hash):
    wrapper=load(root,ref)
    root=local(root,ref['path']).parent
    require(type(wrapper.get('schemaVersion')) is int and wrapper['schemaVersion']==1 and wrapper.get('kind')=='sampled-owner-allocation-diagnostic' and wrapper.get('qualified') is False,
            'A tiny fixture trace or caller source label is not actual owner allocation-site evidence')
    report=load(root,wrapper['report']);owner=load(root,wrapper['owner'])
    require(report.get('kind')=='sampled-allocation-stack-diagnostic' and report.get('status')=='diagnostic-pass' and report.get('qualified') is False and report.get('parser')=='TraceEvent4.0.0','Pinned actual sampled diagnostic unavailable')
    require(owner.get('sourceSha')==source and digest(owner.get('runtimeFingerprint'),'traced owner runtime')==runtime,'Traced source/runtime differs from qualified baseline')
    validate_live_owner_telemetry(owner)
    device=owner['adapterIdentity'];pid=integer(device.get('processId'),'traced producer PID',1)
    require(pid==report.get('expectedPid')==owner['ownerThread'].get('processId'),'Actual traced process/owner identity differs')
    native_tid=integer(owner['ownerThread'].get('nativeThreadId'),'actual traced owner OS thread',1)
    before=load(root,wrapper['processBefore']);after=load(root,wrapper['processAfter']);command=load(root,wrapper['command'])
    for observed in (before,after):
        require(observed.get('alive') is True and observed.get('processId')==pid and observed.get('creationFileTimeUtc')==device.get('processStartedFileTimeUtc') and observed.get('imageSha256')==client_image_hash,
                'Independent OS process/image/creation observations do not bind the actual traced renderer')
    require(command.get('exitCode')==0 and command.get('failure') is None and utc(before['observedAtUtc'])<=utc(command['startedAtUtc'])<=utc(command['completedAtUtc'])<=utc(after['observedAtUtc']),
            'Trace command was not bracketed by the same live producer lifetime')
    argv=command.get('argv');require(type(argv) is list and '--process-id' in argv and argv[argv.index('--process-id')+1]==str(pid) and '--profile' in argv and argv[argv.index('--profile')+1]=='gc-verbose',
            'Sampled diagnostic must attach the pinned profile to that actual producer, not launch an unrelated fixture')
    trace=local(root,wrapper['trace']['path']);require(trace.stat().st_size<=4*1024*1024*1024,'Trace exceeds declared bound')
    import hashlib
    digestor=hashlib.sha256()
    with trace.open('rb') as stream:
        reported_prefix=base64.b64decode(report.get('clock',{}).get('headerBytesBase64',''),validate=True)
        require(12<=len(reported_prefix)<=512,'Original reported trace clock prefix outside bound')
        header=stream.read(len(reported_prefix));digestor.update(header)
        for block in iter(lambda:stream.read(1024*1024),b''):digestor.update(block)
    actual_trace=digestor.hexdigest()
    require(actual_trace==digest(wrapper['trace']['sha256'],'original trace')==report.get('traceSha256Before')==report.get('traceSha256After'),'Actual original trace bytes differ from parser/command evidence')
    verified_execution=verify_trace_execution(root,wrapper,report,source,actual_trace)
    clock=report.get('clock',{})
    require(base64.b64decode(clock.get('headerBytesBase64',''),validate=True)==header and clock.get('headerSha256')==sha256_bytes(header),'Parser header differs from retained original trace')
    command_directory=local(root,wrapper['command']['path']).parent
    for output in command.get('outputs',{}).values():require(sha256_bytes(local(command_directory,output['path']).read_bytes())==output['sha256'],'Raw trace command output changed')
    require(report.get('logEventsLost')==0 and report.get('sourceEventsLost')==0 and not report.get('conversionNotices') and report.get('truncatedStacks')==0,'Sampled diagnostic reports loss/truncation')
    own=[e for e in report.get('events',[]) if e.get('processId')==pid and e.get('nativeThreadId')==native_tid and e.get('resolvedStackAvailable') is True]
    require(own and any(e.get('stack') for e in own),'No resolved sampled allocation stack belongs to the actual native render-owner thread')
    # Build correlation binding ONLY from independently checked command/process/image and immutable owner/trace bytes.
    from allocation_diagnostics import correlate,_derive_raw_qpc_proof
    bound=dict(report,producerBinding=dict(ownerSha256=sha256_bytes(canonical(owner)),traceSha256=actual_trace,sourceSha=source,runtimeFingerprint=owner['runtimeFingerprint'],
        sessionId=device['sessionId'],processId=pid,processStartedFileTimeUtc=device['processStartedFileTimeUtc']))
    correlation=correlate(bound,owner,source_sha=source,runtime_fingerprint=owner['runtimeFingerprint'],producer_session=device['sessionId'])
    require(correlation.get('status')=='compatible-original-clocks','Original trace/owner clock correlation unavailable; no warm-frame site attribution')
    # Refine only AFTER every original provenance, process, header, loss and clock gate.
    # No JSON/caller sameClock flag is read. Unknown builds/modules retain generic behavior.
    raw_proof=_derive_raw_qpc_proof(bound,owner,verified_execution)
    if raw_proof is not None:
        correlation=correlate(bound,owner,source_sha=source,runtime_fingerprint=owner['runtimeFingerprint'],producer_session=device['sessionId'],raw_qpc_proof=raw_proof)
        require(correlation.get('status')=='compatible-original-clocks','Explicit derived raw-QPC proof failed original identity/compatibility checks')
    matched={e['timestampQpc'] for e in correlation['events'] if e.get('status')=='matched' and e.get('frameIndex') is not None}
    require(any(e.get('timestampQpc') in matched for e in own),'No resolved actual native-owner allocation stack matches the declared measured window; startup/ambiguous/outside samples cannot qualify warm sites')
    return dict(status='verified-sampled-owner-sites',events=len(own),frameCorrelation=correlation,scope='Separate sampled stacks; source/thread attribution does not invent exact bytes or normal-run timing')

def reviewed_handoff(record,source,cohort_hash,analysis):
    require(type(record.get('schemaVersion')) is int and record['schemaVersion']==1 and record.get('kind')=='reviewed-baseline-analysis','Wrong reviewed baseline analysis contract')
    require(record.get('sourceSha')==source and record.get('cohortSha256')==cohort_hash and record.get('measuredAnalysisSha256')==sha256_bytes(canonical(analysis)),'Reviewed analysis does not bind exact original cohort/distributions')
    require(record.get('decision')=='accepted' and type(record.get('reviewer')) is str and record['reviewer'] and type(record.get('rationale')) is str and record['rationale'],'Baseline analysis review missing')
    lanes=record.get('wave2Lanes');require(type(lanes) is dict and set(lanes)=={'2_ReduceCpuSubmissionCost','2_QualifyGpuCompositionCost'},'Both original wave2 lane dispositions required')
    for lane in lanes.values():
        require(lane.get('decision') in ('hypothesis','no-change') and type(lane.get('rationale')) is str and lane['rationale'],'Each lane needs a measured hypothesis or honest no-change rationale')
        require(type(lane.get('measuredEvidenceKeys')) is list and lane['measuredEvidenceKeys'] and set(lane['measuredEvidenceKeys'])<=set(analysis),'Lane disposition lacks original measured distributions')
    require(type(record.get('sampledAllocationDiagnosticRefs')) is list and record['sampledAllocationDiagnosticRefs'],'Existing allocation-site ranking requires separately retained sampled diagnostic references')
    return True

def evaluate(cohort,root,repo,trusted_master):
    """Trusted record bytes come from accepted master, never the submitted proposal alone."""
    require(type(cohort.get('schemaVersion')) is int and cohort['schemaVersion']==1 and cohort.get('kind')=='baseline-control-cohort','Wrong baseline cohort contract')
    digest(trusted_master,'trusted master source',40);git(repo,'merge-base','--is-ancestor',trusted_master,'origin/master')
    source=digest(cohort.get('sourceSha'),'A/A source',40);git(repo,'merge-base','--is-ancestor',source,'origin/master')
    protocol=committed(repo,trusted_master,cohort['protocol']);cases=validate_protocol(protocol)
    record_refs=[cohort['protocol'],*cohort['policies'].values()]
    accepted_times=[utc(git(repo,'log','-1','--format=%cI',trusted_master,'--',ref['path']).decode().strip()) for ref in record_refs]
    require(digest(protocol.get('executionSource'),'protocol execution source',40)==source,'Measured source differs from predeclared protocol')
    policies={k:committed(repo,trusted_master,cohort['policies'][k]) for k in ('hardware-stability-policy','allocation-budget','gpu-memory-budget')}
    require(utc(protocol['frozenAtUtc'])>=max(utc(p['reviewedAtUtc']) for p in policies.values()),'Protocol freeze predates reviewed bounds')
    # Reuse only the independently executed exact-source CI importer. No art/future-candidate/lifecycle gate imports.
    gates,imports=validation_imports({'validationEvidence':[cohort['requiredCi']]},Path(root),{'baselineSha':source,'candidateSha':source},{},True)
    require(gates.get('requiredCi',{}).get('status')=='pass','Exact-source required CI is missing/failed')
    runs=cohort.get('runs');require(type(runs) is list and runs,'All retained actual attempts required')
    directories=[r.get('directory') for r in runs];require(len(directories)==len(set(directories)),'A retained run cannot be reused')
    require(cohort.get('attemptInventoryRoot')==protocol['attemptInventoryRoot'],'Caller cannot select a winning attempt subtree')
    schedule=protocol.get('attemptSchedule');require(type(schedule) is list and schedule,'Predeclared exact attempt schedule required')
    scheduled={(r['pair'],r['arm'],r['caseId'],r['producer']):r for r in schedule}
    require(len(scheduled)==len(schedule),'Duplicate planned attempt')
    require(set(scheduled)=={(r['pair'],r['arm'],r['caseId'],r['producer']) for r in runs},'Actual attempt schedule differs from accepted freeze')
    require(all(r['directory']==scheduled[(r['pair'],r['arm'],r['caseId'],r['producer'])]['directory'] for r in runs),'Attempt output directories changed after freeze')
    inventory=local(root,protocol['attemptInventoryRoot'])
    actual={p.parent.resolve() for p in inventory.rglob('attempt.json')}
    require(actual=={local(root,d) for d in directories},'Failed/extra attempts were omitted from the frozen cohort')
    supplemental=protocol['supplementalCoverage']
    require({r.get('mode') for r in supplemental}=={'actual-live-gameplay','observer-on-off'},'Separately frozen gameplay and collector experiment required')
    extras={r['id']:r for r in supplemental};require(len(extras)==len(supplemental) and not set(extras).intersection(cases),'Supplemental cohort IDs must be unique')
    observed={};identities=set();chronology=[];actual_order=[]
    for r in runs:
        frozen_extra=extras.get(r['caseId'])
        case=cases[r['caseId']] if frozen_extra is None else cases[frozen_extra['fixtureCaseId']] if frozen_extra['mode']=='observer-on-off' else None
        pair=integer(r['pair'],'pair',1);arm=r['arm'];producer=r['producer']
        gameplay=producer=='gameplay';collector=producer in ('collector-on','collector-off')
        require(1<=pair<=protocol['pairs'] and arm in ('A','B') and producer in ('native','host','gameplay','collector-on','collector-off'),'Unknown/unplanned run identity')
        key=(pair,arm,r['caseId'],producer);require(key not in observed,'Duplicate attempt identity; winning reruns cannot replace failures')
        directory=local(root,r['directory']);attempt=load(directory,r['attempt'])
        require(attempt.get('status')=='structural-pass' and attempt.get('exitCode')==0 and not attempt.get('failures') and attempt.get('sourceSha')==source,'Retained capture failed or belongs to another source')
        require(attempt.get('diagnosticOnly',False) is False,'Separate stage/attach/setup diagnostics cannot be admitted as qualification timing')
        start,end=utc(attempt['commandStartedAtUtc']),utc(attempt['commandCompletedAtUtc']);require(start<end and utc(protocol['frozenAtUtc'])<=start,'Capture interval invalid or predates freeze')
        require(max(accepted_times)<=start,'Reviewed record bytes were not committed before capture')
        for kind,p in policies.items():validate_policy(p,kind,start)
        payload_before=load(directory,r['payloadInventoryBefore']);payload_after=load(directory,r['payloadInventoryAfter'])
        require(payload_before==payload_after==protocol['payloadInventory'],'Complete binary/runtime payload inventory changed or differs from freeze')
        require(r['payloadInventoryBefore']['sha256']==attempt['payloadInventoryBeforeSha256'] and r['payloadInventoryAfter']['sha256']==attempt['payloadInventoryAfterSha256'],'Actual launcher payload references differ')
        request=load(directory,r['request']);require(sha256_bytes((directory/r['request']['path']).read_bytes())==attempt['requestSha256'],'Actual request differs from launched bytes')
        require(request.get('captureStageDiagnostics',False) is False and request.get('ownerTraceControlDirectory') is None and request.get('captureBoundInputProvenanceVersion') is None,'Intrusive stage/owner-trace/CPU-source provenance requests are separate diagnostics, never baseline timing')
        observation_version=request.get('ownerObservationVersion',0)
        require(type(observation_version) is int and observation_version in (0,1),'Unknown actual owner-observation version')
        require(type(protocol.get('ownerObservationVersion',0)) is int and observation_version==protocol.get('ownerObservationVersion',0),'Actual owner-observation interval differs from frozen protocol')
        if observation_version==0:require(policies['allocation-budget'].get('allocationInterval') in (None,'legacy-payload'),'A cycle policy cannot be applied to legacy payload evidence')
        cold=case is not None and case['runProfile']=='RendererCold';count=128 if cold else 512;warmups=0 if cold else 64
        require(request['outputRoot']==scheduled[key]['launchOutputPath'],'Actual launched output path differs from predeclared attempt')
        require(request['commit']==source and request['samples']==count and request['warmups']==(64 if cold else warmups),'Actual launched window differs from freeze')
        if not gameplay:
            require(request['runProfile']==case['runProfile'] and request['cameraPath']==case['cameraPath'] and request['scenes']==[case['scene']] and request['profiles']==[case['quality']] and request['zooms']==[case['zoom']] and request['displays']==[dict(width=case['width'],height=case['height'])] and request['rasterDpi']==96,'Actual launched case differs')
        expected_mode='CollectorOn' if producer=='collector-on' else 'CollectorOff' if producer=='collector-off' else 'Full'
        require(gameplay or request.get('collectorMode','Full')==expected_mode,'Actual requested optional collector mode differs')
        manifest=load(directory,r['manifest']);require(gameplay or manifest.get('status')=='structural-pass','Actual producer manifest failed')
        seal=manifest.get('runtimeSeal',{});require(type(seal.get('integrityVersion')) is int and seal['integrityVersion']==1 and seal.get('allRecordSizesAndHashesVerified') is True and integer(seal.get('assetCount'),'sealed asset count',1)>0,'Actual producer did not verify sealed inventory')
        digest(seal.get('manifestSha256'),'sealed runtime manifest');digest(seal.get('contentSignature'),'sealed content signature')
        checkpoint=load(directory,r['checkpoint']);binding=load(directory,r['binding']);cuda=load(directory,r['cudaInventory'])
        sensor_raw=local(directory,r['bindingSensorRaw']['path']).read_bytes();require(sha256_bytes(sensor_raw)==r['bindingSensorRaw']['sha256'],'Binding sensor raw changed')
        sensor_command=load(directory,r['bindingSensorCommand']);sensor_argv=sensor_command['argv']
        from provider import FIELDS
        require(sensor_argv[1:]==['--query-gpu='+FIELDS,'--format=csv,noheader,nounits'],'Literal provider query schema changed')
        verify_command(directory,r['bindingSensorCommand'],sensor_argv)
        require(sensor_command['outputs']['stdout']['sha256']==r['bindingSensorRaw']['sha256'],'Binding inventory differs from exact successful query output')
        version=verify_command(directory,r['bindingProviderVersionCommand'],[sensor_argv[0],'--version'])
        require(binding['sensorExecutableSha256Before']==binding['sensorExecutableSha256After']==attempt['sensorExecutableSha256'] and binding['providerVersionSha256']==attempt['providerVersionSha256']==version['outputs']['stdout']['sha256'],'Actual binding/attempt provider binary/version differ')
        native_bytes=local(directory,r['checkpoint']['path']).read_bytes()
        recomputed=bind_adapter(native_bytes,cuda,parse_inventory(sensor_raw),binding['processBefore'],binding['processAfter'],binding['commandContext'])
        require(recomputed['exactPhysicalMappingAvailable'] is True and all(recomputed[k]==binding[k] for k in recomputed),'Exact original lifetime/adapter proof differs')
        require(binding['rendererIdentity']['processId']==attempt['processId'] and binding['commandContext']['startedAtUtc']==attempt['commandStartedAtUtc'] and binding['commandContext']['sourceSha']==source,'Physical proof belongs to another launched producer')
        require(binding['commandContext']['requestSha256']==attempt['requestSha256'] and binding['commandContext']['argv']==attempt['argv'],'Physical proof command/request mismatch')
        runtime=digest(manifest['runtimeFingerprint'],'actual runtime');
        if not gameplay:require(digest(case['recipe']['runtimeFingerprint'],'frozen actual recipe runtime')==runtime,'Actual runtime bytes differ from frozen recipe')
        require(runtime==digest(attempt['runtimeFingerprint'],'launched runtime')==digest(checkpoint['runtimeFingerprint'],'checkpoint runtime'),'Runtime bytes differ')
        identity=(attempt['clientExecutableSha256'],attempt['clientAssemblySha256'],runtime,seal['manifestSha256'],seal['contentSignature'],attempt['sensorExecutableSha256'],attempt['providerVersionSha256'],binding['uuid'],binding['pciBusId'])
        identities.add(tuple(str(x).lower() for x in identity));require(len(identities)==1,'A/A binary/runtime/seal/provider/hardware identities changed')
        if gameplay:
            require(frozen_extra is not None and frozen_extra['mode']=='actual-live-gameplay' and attempt.get('captureMode')=='actual-live-gameplay' and attempt['argv'][1:]==['--smoke-test'],'Actual live session launch mode/origin differs')
            owner=manifest;cpu=validate_gameplay(owner,frozen_extra);gpu=owner['gpuCompletions']
            require(owner['sourceSha']==source and digest(owner['runtimeFingerprint'],'owner runtime')==runtime and owner['adapterIdentity']==binding['rendererIdentity'] and owner['adapterBinding']['binding']==binding,'Actual gameplay source/runtime/device/session differs')
            exact_join(cpu,[dict(f,index=i,ordinal=64+i) for i,f in enumerate(cpu)],gpu,64)
            payload=[(f['endTick']-f['startTick'])*1000/owner['frequencyHz'] for f in cpu]
            measured_start,measured_end=sample_times(owner['clockAnchor'],cpu,owner['frequencyHz']);pixel=None
            for f in cpu:
                for name in (() if observation_version==1 else ('processAllocatedBytes','threadAllocatedBytes')):require(integer(f[name],name)<=policies['allocation-budget']['maximumPerSample'][name],'Gameplay full-process/thread allocation budget exceeded')
                for name in MEMORY_COUNTERS:require(integer(f['memory'][name],name)<=policies['gpu-memory-budget']['maximumPerSample'][name],'Gameplay logical memory budget exceeded')
        elif collector:
            require(frozen_extra is not None and frozen_extra['mode']=='observer-on-off' and (producer=='collector-on')==(arm=='A'),'Actual control arm was changed after freeze')
            fixture=manifest['fixture'];validate_recipe(fixture,case)
            require(manifest['sourceSha']==source and fixture['adapterBinding']['binding']==binding and fixture['readback']['adapterIdentity']==binding['rendererIdentity'] and fixture['profile']==case['effectiveProfile'],'Collector actual producer/recipe/profile/device differs')
            probe=fixture['collectorControl'];cpu=validate_collector_probe(probe,fixture,case,producer=='collector-on');gpu=[]
            if producer=='collector-off':require(manifest.get('owner') is None and manifest.get('ownerSha256') is None and 'owner' not in r,'OFF must not invent owner allocation/GPU evidence')
            else:
                owner=load(directory,r['owner']);require(r['owner']['sha256'].lower()==digest(manifest['ownerSha256'],'control owner') and owner['sourceSha']==source,'ON actual owner stream missing/changed')
                validate_live_owner_telemetry(owner);exact_join(owner['frames'],fixture['frames'],owner['gpuCompletions'],64)
                require([tuple_of(f['submission']) for f in cpu]==[tuple_of(f) for f in owner['frames']],'Common probe and optional owner observe different frames')
            readback=fixture['readback'];verify_png(local(directory,r['manifest']['path']).parent,readback,case)
            pixel=digest(readback['pixelSha256'],'control pixels');require(readback['repeatPixelsEqual'] is True and len(readback['records'])==8 and all(digest(x['pixelSha256'],'control repeat')==pixel for x in readback['records']),'Control repeat pixels differ')
            payload=[(f['endTick']-f['startTick'])*1000/probe['frequencyHz'] for f in cpu]
            measured_start,measured_end=sample_times(probe['clockAnchor'],cpu,probe['frequencyHz'])
        elif producer=='host':
            fixture=manifest['fixture'];owner=load(directory,r['owner']);require(digest(manifest['ownerSha256'],'owner')==r['owner']['sha256'].lower(),'Owner hash differs from producer')
            require(owner['sourceSha']==source and digest(owner['runtimeFingerprint'],'owner runtime')==runtime and owner.get('failedFixtureAttempt') is None,'Owner source/runtime/failed state differs')
            validate_live_owner_telemetry(owner);validate_recipe(fixture,case)
            require(fixture['adapterBinding']['binding']==binding and fixture['readback']['adapterIdentity']==binding['rendererIdentity']==owner['adapterIdentity'],'Host actual device/session identity differs')
            cpu=owner['frames'];ack=fixture['frames'];gpu=owner['gpuCompletions'];exact_join(cpu,ack,gpu,warmups)
            require(owner['window']['warmups']==warmups and len(cpu)==count,'Host cold/warm window differs')
            for i,a in enumerate(ack):
                require(a['camera']==case['sampleCameras'][i] and a['width']==case['width'] and a['height']==case['height'],'Actual per-frame camera ACK differs from frozen path')
                limits=policies['gpu-memory-budget']['assumptions']
                require(a['bufferCount']==limits['frameBufferCount'] and integer(a['work']['drawnParts'],'actual expanded drawn parts')<=limits['maximumExpandedDrawnParts'] and integer(a['hudVertices'],'actual HUD vertices')<=limits['maximumHudVertices'],'Measured capacity assumptions are outside the reviewed fixed-fixture ledger')
            require(fixture['profile']==case['effectiveProfile'],'Actual host effective profile differs')
            if case.get('coverage')=='mixed-material-skin-mechanics-effects':validate_mixed_consumption(ack,case,'host',cpu)
            readback=fixture['readback'];require(readback['repeatPixelsEqual'] is True and len(readback['records'])==8,'All eight repeated host readbacks required')
            verify_png(local(directory,r['manifest']['path']).parent,readback,case)
            pixel=digest(readback['pixelSha256'],'host pixels');require(all(digest(x['pixelSha256'],'repeat')==pixel for x in readback['records']),'Host repeated pixels differ')
            payload=[(f['endTick']-f['startTick'])*1000/owner['frequencyHz'] for f in cpu]
            measured_start,measured_end=sample_times(owner['clockAnchor'],cpu,owner['frequencyHz'])
            if not cold:
                for f in cpu:
                    for name in (() if observation_version==1 else ('processAllocatedBytes','threadAllocatedBytes')):
                        require(integer(f[name],name)<=policies['allocation-budget']['maximumPerSample'][name],'Exact full-frame allocation exceeds reviewed maximum')
                    for name in MEMORY_COUNTERS:
                        require(integer(f['memory'][name],name)<=policies['gpu-memory-budget']['maximumPerSample'][name],'Logical memory exceeds reviewed maximum')
        else:
            require(manifest['commit']==source and str(manifest.get('buildIdentity','')).lower().endswith(source) and len(manifest['captures'])==1,'Native producer actual compiled source/case mismatch')
            require(manifest['adapterIdentity']==binding['rendererIdentity'] and manifest['adapterBinding']['binding']==binding,'Native actual device/session differs')
            sdk=parse_json_bytes(git(repo,'show',source+':global.json'))['sdk']['version']
            actual_hardware=native_identity(manifest,sdk)['hardwareIdentitySha256']
            require(actual_hardware==digest(protocol['nativeHardwareIdentitySha256'],'predeclared actual native hardware'),'Actual source-pinned SDK/environment/runtime/device/debug/driver hardware identity differs')
            capture=manifest['captures'][0];validate_recipe(capture,case);require(capture['profile']==case['effectiveProfile'],'Native effective profile differs')
            require(capture['repeatPixelsEqual'] is True and capture['repeatCaptures']==8 and len(capture['captureInputs'])==8,'All eight native repeated readbacks required')
            verify_png(local(directory,r['manifest']['path']).parent,capture,case)
            pixel=digest(capture['pixelSha256'],'native pixels');require(all(digest(x['pixelSha256'],'repeat')==pixel for x in capture['captureInputs']),'Native repeated pixels differ')
            perf=capture['performance'];cpu=validate_native_samples(perf,count);gpu=perf['rawGpuTiming']['completions'];validate_raw_gpu_timing(perf['rawGpuTiming'])
            require(len(cpu)==count and capture['run']['warmupFrames']==warmups and [f.get('index') for f in cpu]==list(range(count)),'Native cold/warm sample indices differ')
            mapped=[dict(f['actualSubmission'], index=i) for i,f in enumerate(cpu)]
            exact_join(mapped,[dict(f['actualSubmission'],index=i,ordinal=warmups+i) for i,f in enumerate(cpu)],gpu,warmups)
            require(all(f['actualCamera']==case['sampleCameras'][i] for i,f in enumerate(cpu)),'Actual native per-frame camera differs from frozen path')
            if case.get('coverage')=='mixed-material-skin-mechanics-effects':validate_mixed_consumption(cpu,case,'native')
            payload=[f['milliseconds'] for f in cpu]
            measured_start,measured_end=sample_times(perf['clockAnchor'],[f['measuredInterval'] for f in cpu],perf['cpuTimestampFrequencyHz'])
        cycle_result=None
        if observation_version==1:
            from owner_observations import validate_cycles,cycle_times,validate_cycle_budget
            from post_window_control import validate_ready,validate_retained,json_exact
            # This is an additive endpoint contract. Legacy default timing and policy inputs remain unchanged.
            post=directory/'post-window';ready_raw=(post/'ready.json').read_bytes();ready=validate_ready(ready_raw,native_bytes)
            actual_frequency=owner['frequencyHz'] if gameplay or producer=='host' or producer=='collector-on' else probe['frequencyHz'] if collector else perf['cpuTimestampFrequencyHz']
            require(type(ready.get('frequencyHz')) is int and ready['frequencyHz']==actual_frequency,'Ordinary ready frequency differs from actual original producer clock')
            actual_composition=owner.get('observedComposition') if gameplay else dict(profile=fixture['profile'],lighting=fixture['lighting'],atmosphere=fixture['atmosphere']) if producer=='host' or collector else capture.get('observedComposition')
            profile_raw=base64.b64decode(ready['caseIdentity']['profileInputBytesBase64'],validate=True)
            require(actual_composition is not None and json_exact(parse_json_bytes(profile_raw),actual_composition),'Post-window actual composition differs from producer final evidence')
            if producer=='native':
                require(ready['endpointKind']=='native-legacy-frame' and ready['lastCycleEndTick'] is None,'Native cannot invent a ClientRenderHost owner-cycle endpoint')
                last_submission={n:cpu[-1]['actualSubmission'][n] for n in ('submission','submittedFence','generation','resourceGeneration')}
                measured_last=cpu[-1]['measuredInterval']['endTick'];cycle_last=None
                control=manifest.get('postWindowControl')
            else:
                cycle_data=fixture['ownerCycles'] if collector or producer=='host' else owner['ownerCycles']
                cycle_frames=owner['frames'] if producer!='collector-off' else cpu
                require(cycle_data.get('preciseCountersAvailable') is (producer!='collector-off'),'Owner-cycle precise mode differs from actual Full/On/Off producer')
                cycle_result=validate_cycles(cycle_data,cycle_frames,expected_session=owner['frames'][-1]['simulation']['expectedSession'] if gameplay else None,expected_hwnd=checkpoint['nativeWindowHandle'],producer_kind='actual-live-session' if gameplay else 'authored-world-fixture',warmups=warmups,prefix_frames=owner.get('beforeWindow') if gameplay else None)
                if producer in ('host','gameplay') and not cold:validate_cycle_budget(cycle_result,policies['allocation-budget'])
                require(ready['endpointKind']=='owner-cycle-v1','Host control endpoint is not the actual v1 full cycle')
                measured_start,measured_end=cycle_times(owner['clockAnchor'] if producer!='collector-off' else probe['clockAnchor'],owner['frequencyHz'] if producer!='collector-off' else probe['frequencyHz'],cycle_result)
                last_submission=cycle_result['lastSubmission'];cycle_last=cycle_result['lastTick']
                measured_last=(owner['frames'] if producer!='collector-off' else cpu)[-1]['endTick']
                control=owner.get('postWindowControl') if gameplay else fixture.get('postWindowControl')
            expected_case=dict(kind='actual-live-session' if gameplay else 'native-reference' if producer=='native' else 'authored-world-fixture',
                caseKey='session-'+str(owner['frames'][-1]['simulation']['expectedSession']) if gameplay else capture['key'] if producer=='native' else fixture['key'],
                recipeAvailable=not gameplay,recipeFingerprint=None if gameplay else capture['recipeFingerprint'] if producer=='native' else fixture['recipeFingerprint'],
                profileFingerprint=sha256_bytes(profile_raw).upper(),simulationSession=owner['frames'][-1]['simulation']['expectedSession'] if gameplay else None,
                sessionOriginFingerprint=owner['sessionOrigin']['fingerprint'] if gameplay else None,profileInputBytesBase64=base64.b64encode(profile_raw).decode())
            verified_control=validate_retained(post,native_bytes,binding,last_submission=last_submission,last_measured_end=measured_last,last_cycle_end=cycle_last,
                case_identity=expected_case,sample_count=count,command_completed_at=attempt['commandCompletedAtUtc'])
            require(control is not None and control.get('completed') is True and digest(control['receipt']['readySha256'],'producer ready')==verified_control['readySha256'] and digest(control['receipt']['ackSha256'],'producer ACK')==verified_control['ackSha256'],'Producer final ordinary control receipt differs')
        require(start<=measured_start<measured_end<=end,'Original sampled/full-cycle window lies outside actual command')
        rows=read_journal(directory/'sensors');require(all(row['sample']['command']['argv']==sensor_argv and row['sample']['command']['exitCode']==0 and row['sample']['command']['timedOut'] is False for row in rows),'Physical journal query/tool/exit differs from actual binding provider')
        validate_controls(rows,measured_start,measured_end,binding,policies['hardware-stability-policy'],load(directory,r['preWindowControl']),checkpoint['nativeWindowHandle'])
        actual_device=owner['actualDevice'] if producer in ('host','gameplay') else fixture['device'] if collector else capture['graphics']['device']
        require(actual_device.get('isSoftwareAdapter') is False,'Software adapter cannot qualify physical baseline')
        clock_frequency=owner['frequencyHz'] if producer in ('host','gameplay') else probe['frequencyHz'] if collector else perf['cpuTimestampFrequencyHz']
        measured=measured_distributions(producer,cpu,gpu,clock_frequency,setup=manifest.get('setupMilliseconds') if producer=='host' else capture.get('run') if producer=='native' else None)
        observed[key]=dict(ownerCycleBundleCpu=distribution([sample['bundleReadEndTicks']*1000/clock_frequency for sample in cycle_result['samples']]) if cycle_result is not None else None,measured=measured,device=actual_device,cpu=distribution(payload),gpu=distribution([(g['endTick']-g['startTick'])*1000/g['frequencyHz'] for g in gpu]),pixel=pixel,profile=case['effectiveProfile'] if case is not None else None)
        chronology.append((start,end,pair,arm));actual_order.append((start,key))
    expected={(p,a,c,producer) for p,a,c,producer in product(range(1,protocol['pairs']+1),('A','B'),cases,('native','host'))}
    for extra_id,extra in extras.items():
        for p,arm in product(range(1,protocol['pairs']+1),('A','B')):
            expected.add((p,arm,extra_id,'gameplay' if extra['mode']=='actual-live-gameplay' else 'collector-on' if arm=='A' else 'collector-off'))
    require(set(observed)==expected,'Full native/host paired matrix missing; cold is independent of warm budgets')
    frozen_keys=[(r['pair'],r['arm'],r['caseId'],r['producer']) for r in schedule]
    require([key for _,key in sorted(actual_order)]==frozen_keys,'Actual command order differs from exact predeclared case/pair schedule')
    blocks=[]
    for key in frozen_keys:
        block=key[:2]
        if not blocks or blocks[-1]!=block:blocks.append(block)
    require(blocks==[(p,arm) for p in range(1,protocol['pairs']+1) for arm in (('A','B') if p%2 else ('B','A'))],'Frozen schedule does not declare contiguous alternating pair/arm blocks')
    ordered=sorted(chronology);require(all(ordered[i][1]<=ordered[i+1][0] for i in range(len(ordered)-1)),'Capture commands overlap')
    for p in range(1,protocol['pairs']+1):
        blocks=[a for _,_,pair,a in ordered if pair==p];require(blocks and blocks==sorted(blocks,reverse=p%2==0),'Predeclared reversed A/B pair order was changed')
        for c,producer in product(cases,('native','host')):
            a,b=(observed[(p,arm,c,producer)] for arm in ('A','B'))
            require(a['pixel']==b['pixel'],'A/A fixed pixels differ')
            for metric in ('cpu','gpu'):
                for statistic in ('mean','p50','p95','p99','maximum'):stable_pair(a[metric][statistic],b[metric][statistic])
            require(observed[(p,'A',c,'native')]['device']==observed[(p,'A',c,'host')]['device']==observed[(p,'B',c,'native')]['device']==observed[(p,'B',c,'host')]['device'],'Actual native/host device/debug capabilities differ')
            require(observed[(p,'A',c,'native')]['pixel']==observed[(p,'A',c,'host')]['pixel'] and observed[(p,'B',c,'native')]['pixel']==observed[(p,'B',c,'host')]['pixel'],'Actual host/native fixed recipe pixels differ')
    overhead=[];cycle_overhead=[]
    for extra_id,extra in extras.items():
        for p in range(1,protocol['pairs']+1):
            a=observed[(p,'A',extra_id,'gameplay' if extra['mode']=='actual-live-gameplay' else 'collector-on')]
            b=observed[(p,'B',extra_id,'gameplay' if extra['mode']=='actual-live-gameplay' else 'collector-off')]
            require(a['device']==b['device'],'Supplemental actual device differs')
            if extra['mode']=='actual-live-gameplay':
                for metric in ('cpu','gpu'):
                    for statistic in ('mean','p50','p95','p99','maximum'):stable_pair(a[metric][statistic],b[metric][statistic])
            else:
                require(a['pixel']==b['pixel'],'Actual collector-on/off fixed recipe pixels differ')
                overhead.append(dict(id=extra_id,pair=p,on=a['cpu'],off=b['cpu'],meanOverheadPercent=(a['cpu']['mean']/b['cpu']['mean']-1)*100,
                    scope='Legacy common-probe duration only; excludes optional v1 condition-edge observation cost; not an allocation budget or all-instrumentation-off claim'))
                if a['ownerCycleBundleCpu'] is not None or b['ownerCycleBundleCpu'] is not None:
                    require(a['ownerCycleBundleCpu'] is not None and b['ownerCycleBundleCpu'] is not None,'Collector arms select different common v1 interval contracts')
                    on,off=a['ownerCycleBundleCpu'],b['ownerCycleBundleCpu']
                    require(off['mean']>0,'V1 common bundle duration is unavailable')
                    cycle_overhead.append(dict(id=extra_id,pair=p,on=on,off=off,meanOverheadPercent=(on['mean']/off['mean']-1)*100,scope='V1 shared ReadEnd-to-ReadEnd arrival bundles include idle cycles and one condition-edge observer per cycle; initial priming outside; not summed with legacy common probe; descriptive only'))
    analysis={'|'.join(map(str,key)):row for key,row in observed.items()}
    ready=False;analysis_status='open: reviewed original phase/pass/wait/cold/allocation-site ranking and both wave2 lane dispositions required'
    if cohort.get('reviewedAnalysis') is not None:
        try:
            reviewed=committed(repo,trusted_master,cohort['reviewedAnalysis'])
            bound=dict(cohort);bound.pop('reviewedAnalysis',None)
            reviewed_handoff(reviewed,source,sha256_bytes(canonical(bound)),analysis)
            diagnostics=[validate_sampled_owner_diagnostic(root,ref,source,next(iter(identities))[2],next(iter(identities))[0]) for ref in reviewed['sampledAllocationDiagnosticRefs']]
            ready=True;analysis_status='reviewed-bound-original-baseline-analysis-with-verified-sampled-owner-sites'
        except (ValueError,KeyError,TypeError,OSError,IndexError) as error:
            analysis_status='open: '+str(error)
    return dict(schemaVersion=1,kind='baseline-admission',status='baseline-controls-accepted',baselineControlsAccepted=True,physicalAcceptance='accepted-controls-only',wave2Ready=ready,baselineAnalysisStatus=analysis_status,measuredAnalysis=analysis,
                sourceSha=source,trustedMasterSource=trusted_master,cases=len(cases),pairs=protocol['pairs'],attempts=len(runs),requiredCi=imports,collectorOverhead=overhead,ownerCycleCollectorOverhead=cycle_overhead,
                scope='Baseline-only A/A controls; no optimization ranking, future-candidate, lifecycle or human art acceptance inferred',logicalMemoryCounters=list(MEMORY_COUNTERS),physicalAllocationCoverage='unavailable; not confused with logical budget accounting')
