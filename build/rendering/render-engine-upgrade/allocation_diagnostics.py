"""Separate pinned sampled-stack diagnostics. Never a timing or exact-byte budget substitute."""
import base64,json,subprocess,sys
from dataclasses import dataclass,asdict
from pathlib import PureWindowsPath
from datetime import datetime,timezone
from pathlib import Path
from baseline_contracts import require,read_json,write_new,sha256_bytes,utc
from provider import run_record,resolve_executable

RAW_QPC_RULE='windows-coreclr-10.0.13-traceevent4-retained-parser-raw-qpc-v1'
_RAW_PARSER_SOURCE='ec5db1892799977bea3b7908ce8b8688ce94b49b'
_RAW_PARSER_BUILD='0.1.539-dev+'+_RAW_PARSER_SOURCE
_RAW_PARSER_SHA='63808a724288c49f3c5be45412687f9861d21a9c53c18453af2d7604545f3d17'
_RAW_DEPS_SHA='7ea4a4da11c3e5c561be35d934e5f7654ac14cc381f3773e0b3e02b54f79f157'
_RAW_CONTRACT='docs/rendering/render-engine-upgrade/evidence/qualification/RawQpcCorrelationContract.md'

@dataclass(frozen=True)
class _VerifiedTraceExecution:
    # Constructed by baseline admission ONLY after its original execution gates.
    report_sha256: str
    report_file_sha256: str
    trace_sha256: str
    source_sha: str
    dependencies_sha256: str
    execution_provenance_sha256: str

@dataclass(frozen=True)
class _RawQpcProof:
    rule: str
    report_sha256: str
    owner_sha256: str
    binding_sha256: str
    target_runtime_modules: tuple
    contract_sha256: str
    execution: _VerifiedTraceExecution

    def evidence(self):
        return dict(**asdict(self),contractPath=_RAW_CONTRACT,
            trust='Ordinary installed Windows CoreCLR runtime/module-version trust; historical runtime module contents are not cryptographically attested',
            historicalRuntimeModuleHashesAvailable=False)

def _report_hash(trace):
    from baseline_contracts import canonical
    return sha256_bytes(canonical({k:v for k,v in trace.items() if k!='producerBinding'}))

def _derive_raw_qpc_proof(trace,owner,execution):
    """Internal post-gate derivation, never consumes a caller sameClock assertion."""
    from baseline_contracts import canonical
    require(type(execution) is _VerifiedTraceExecution,'Original execution gates have not produced typed verified evidence')
    require(execution.report_sha256==_report_hash(trace) and execution.trace_sha256==trace['traceSha256Before']==trace['traceSha256After'],'Verified execution belongs to another report/trace')
    require(all(type(trace.get(k)) is int and trace[k]==0 for k in ('logEventsLost','sourceEventsLost','truncatedStacks')) and not trace.get('conversionNotices'),'Original trace reports loss/truncation')
    binding=trace['producerBinding']
    require(execution.source_sha==binding['sourceSha'],'Verified parser execution/source binding differs')
    generic=correlate(trace,owner,source_sha=binding['sourceSha'],runtime_fingerprint=binding['runtimeFingerprint'],producer_session=binding['sessionId'])
    require(generic['status']=='compatible-original-clocks','Original header/frequency/UTC/process/source clock gates did not pass')
    if (trace.get('parser')!='TraceEvent4.0.0' or trace.get('parserBuildIdentity')!=_RAW_PARSER_BUILD or
        trace.get('parserAssemblySha256')!=_RAW_PARSER_SHA or execution.dependencies_sha256!=_RAW_DEPS_SHA or
        execution.source_sha!=_RAW_PARSER_SOURCE or trace['clock']['format']!='pinned-nettrace-fastserialization-trace-v4'):
        return None # A different build/format needs its own reviewed source-path contract.
    modules=set();versions=set();corelibs=set();roots=set()
    for event in trace['events']:
        require(event['processId']==trace['expectedPid'],'Target runtime module evidence contains another PID')
        for frame in event.get('stack',[]):
            module=frame.get('module')
            if not isinstance(module,str) or not module:continue
            path=PureWindowsPath(module)
            # Only actual target-event module paths, never another process preflight/runtime string.
            parts=path.parts
            for index,part in enumerate(parts):
                if part.casefold()!='microsoft.netcore.app':continue
                if not path.is_absolute() or index<1 or parts[index-1].casefold()!='shared' or len(parts)!=index+3:return None
                version,name=parts[index+1:];versions.add(version);modules.add(str(path));roots.add(str(path.parent).casefold())
                if name.casefold()=='system.private.corelib.dll':corelibs.add(str(path.parent).casefold())
    if versions!={'10.0.13'} or len(corelibs)!=1 or roots!=corelibs:return None
    contract=Path(__file__).resolve().parents[3]/_RAW_CONTRACT
    return _RawQpcProof(RAW_QPC_RULE,_report_hash(trace),sha256_bytes(canonical(owner)),sha256_bytes(canonical(binding)),tuple(sorted(modules)),sha256_bytes(contract.read_bytes()),execution)

def correlate(trace,owner,*,source_sha,runtime_fingerprint,producer_session,raw_qpc_proof=None):
    """Only join already verified original clocks; unknown clocks remain explicit unavailable."""
    try:
        require(trace.get('status')=='diagnostic-pass','Trace is incomplete')
        clock=trace['clock'];anchor=owner['clockAnchor'];thread=owner['ownerThread']
        from trace_clock_header import decode_clock_header
        header=base64.b64decode(clock['headerBytesBase64'],validate=True)
        require(sha256_bytes(header)==clock['headerSha256'],'Original raw trace header hash unavailable')
        decoded=decode_clock_header(header,trace['traceLength'],trace['expectedPid'])
        for name,value in decoded.items():
            if name=='headerLength' and name not in clock:continue # Historical v6 evidence did not export this optional field.
            require(type(clock.get(name)) is type(value) and clock[name]==value,'Clock summary differs from independently decoded original header: '+name)
        binding=trace.get('producerBinding')
        require(type(binding) is dict,'Independent original trace command/image/process/source/session binding unavailable; clock compatibility alone cannot attest interval attribution')
        from baseline_contracts import canonical
        require(binding.get('ownerSha256')==sha256_bytes(canonical(owner)) and binding.get('traceSha256')==trace.get('traceSha256Before')==trace.get('traceSha256After'),'Trace/owner immutable run binding unavailable')
        require(binding.get('sourceSha')==source_sha and binding.get('runtimeFingerprint')==runtime_fingerprint and binding.get('sessionId')==producer_session and binding.get('processId')==trace['expectedPid'] and binding.get('processStartedFileTimeUtc')==owner['adapterIdentity']['processStartedFileTimeUtc'],'Original traced producer identity differs')
        require(clock.get('available') is True and clock.get('format') in ('pinned-nettrace-v6-raw-header','pinned-nettrace-fastserialization-trace-v4'),'Original supported trace clock unavailable')
        frequency=clock['tickFrequency']
        require(type(frequency) is int and frequency>0 and frequency==anchor['frequencyHz'],'Original trace and owner frequencies differ')
        require(owner['sourceSha']==source_sha and owner['runtimeFingerprint']==runtime_fingerprint and owner['adapterIdentity']['sessionId']==producer_session,'Original producer source/runtime/session differs')
        require(type(thread['nativeThreadId']) is int and thread['nativeThreadId']>0 and thread['processId']==trace['expectedPid'],'Actual process/native thread identity unavailable')
        import struct
        fields=struct.unpack('<8H',base64.b64decode(clock['utcSystemTimeBytesBase64'],validate=True))
        origin=datetime(fields[0],fields[1],fields[3],fields[4],fields[5],fields[6],fields[7]*1000,tzinfo=timezone.utc)
        before,after=anchor['beforeTick'],anchor['afterTick'];require(type(before) is int and type(after) is int and before<=after,'Invalid owner UTC/QPC bracket')
        # Nettrace SYSTEMTIME milliseconds plus original owner read bracket, not offline parser timing.
        delta=(utc(anchor['utc'])-origin).total_seconds()*frequency
        require(before-clock['syncTicks']-frequency/1000<=delta<=after-clock['syncTicks']+frequency/1000,'Original UTC/QPC anchors are incompatible')
        uncertainty=(after-before)+frequency/1000
        if raw_qpc_proof is not None:
            require(type(raw_qpc_proof) is _RawQpcProof and type(raw_qpc_proof.execution) is _VerifiedTraceExecution,'Invalid explicit raw-QPC proof; caller assertions cannot bypass generic uncertainty')
            require(raw_qpc_proof.rule==RAW_QPC_RULE and raw_qpc_proof.report_sha256==_report_hash(trace)==raw_qpc_proof.execution.report_sha256 and
                raw_qpc_proof.owner_sha256==sha256_bytes(canonical(owner)) and raw_qpc_proof.binding_sha256==sha256_bytes(canonical(binding)) and
                raw_qpc_proof.execution.trace_sha256==trace['traceSha256Before'] and raw_qpc_proof.execution.source_sha==source_sha,
                'Explicit raw-QPC proof belongs to another original trace/report/owner/binding')
            expected=_derive_raw_qpc_proof(trace,owner,raw_qpc_proof.execution)
            require(expected is not None and expected==raw_qpc_proof,'Explicit raw-QPC proof has unsupported/conflicting runtime/parser/contract evidence')
        for frame in owner['frames']:
            require(type(frame['startTick']) is int and type(frame['endTick']) is int and frame['startTick']<frame['endTick'],'Owner sample boundaries must be ordered original integers')
        joined=[]
        for event in trace['events']:
            require(event['processId']==thread['processId'],'Trace contains another process')
            if event['nativeThreadId']!=thread['nativeThreadId']:continue
            tick=event['timestampQpc'];require(type(tick) is int,'Raw trace tick unavailable')
            if raw_qpc_proof is None:
                matches=[f['index'] for f in owner['frames'] if f['startTick']<=tick<f['endTick']]
                near=any(abs(tick-f[key])<=uncertainty for f in owner['frames'] for key in ('startTick','endTick'))
            else:
                matches=[f['index'] for f in owner['frames'] if f['startTick']<tick<f['endTick']]
                near=any(tick==f[key] for f in owner['frames'] for key in ('startTick','endTick'))
            require(len(matches)<=1,'Owner sample intervals overlap')
            joined.append(dict(timestampQpc=tick,frameIndex=None if near or not matches else matches[0],status='ambiguous-boundary' if near else 'matched' if matches else 'outside-window'))
        return dict(status='compatible-original-clocks',uncertaintyTicks=0 if raw_qpc_proof is not None else uncertainty,
            utcAnchorSanityUncertaintyTicks=uncertainty,boundaryRule='strict-integer-interior-exact-ties-ambiguous' if raw_qpc_proof is not None else 'conservative-utc-anchor-uncertainty',
            rawQpcSourceProof=raw_qpc_proof.evidence() if raw_qpc_proof is not None else None,events=joined,exactAllocationBytesAvailable=False)
    except (ValueError,KeyError,TypeError) as error:return dict(status='unavailable',reason=str(error),exactAllocationBytesAvailable=False)

def positive_sample(event):
    payload=event['payload']
    names=('allocationAmount64','AllocationAmount') if event['family']=='GCAllocationTick' else ('ObjectSize',)
    return any(type(payload.get(name)) is int and payload[name]>0 for name in names)

def integrate(tool,fixture,parser,output,repository):
    output=Path(output).resolve();output.mkdir(parents=True,exist_ok=False)
    record=dict(kind='sampled-allocation-integration',qualified=False,status='failed',startedAtUtc=datetime.now(timezone.utc).isoformat())
    try:
        repo=Path(repository).resolve()
        source=subprocess.run(['git','rev-parse','HEAD'],cwd=repo,capture_output=True,check=True).stdout.decode().strip()
        clean=not subprocess.run(['git','status','--porcelain','--untracked-files=normal'],cwd=repo,capture_output=True,check=True).stdout.strip()
        record.update(sourceSha=source,sourceTreeClean=clean);require(clean,'Real integration requires clean committed tooling/fixture source')
        tool=resolve_executable(tool);fixture=Path(fixture).resolve(strict=True);parser=Path(parser).resolve(strict=True)
        from capture_runner import payload_inventory
        tool_before=payload_inventory(Path(tool).parent);parser_before=payload_inventory(parser.parent)
        tool_before_ref=write_new(output/'tool-payload-before.json',tool_before)
        parser_before_ref=write_new(output/'parser-payload-before.json',parser_before)
        inputs={str(p):sha256_bytes(p.read_bytes()) for p in (Path(tool),fixture,parser,fixture.with_suffix('.deps.json'),parser.with_suffix('.deps.json'))}
        record['inputSha256Before']=inputs
        version,_=run_record([tool,'--version'],output,'tool-version');require(version.decode(errors='replace').strip().startswith('10.0.750501'),'Wrong pinned dotnet-trace version')
        run_record([tool,'list-profiles'],output,'tool-profiles')
        trace=output/'allocations.nettrace';pidfile=output/'fixture.pid'
        run_record([tool,'collect','--profile','gc-verbose','--buffersize','256','--output',str(trace),'--','dotnet',str(fixture),str(pidfile)],output,'collect',timeout=120)
        pid=int(pidfile.read_text());require(pid>0,'Actual fixture PID missing')
        parsed=output/'parsed';run_record(['dotnet',str(parser),str(trace),str(parsed),str(pid)],output,'parse',timeout=120)
        report=read_json(parsed/'allocation-stacks.json')
        own=report['events'];require(report['status']=='diagnostic-pass' and own,'No complete sampled diagnostic')
        require(all(e['processId']==pid and not e['stackTruncated'] for e in own),'Wrong process/truncated own stack')
        methods=[f.get('name','') for e in own for f in e['stack']]
        require(any('AllocationFixture.AllocateA' in name or 'AllocationFixture.AllocateB' in name for name in methods),'Actual fixture allocation method stack unavailable')
        require(any(positive_sample(e) for e in own),'No positive sampled payload')
        record.update(status='diagnostic-pass',actualPid=pid,traceSha256=sha256_bytes(trace.read_bytes()),parsedSha256=sha256_bytes((parsed/'allocation-stacks.json').read_bytes()))
        after={p:sha256_bytes(Path(p).read_bytes()) for p in inputs};record['inputSha256After']=after;require(inputs==after,'Tool/fixture/parser changed during diagnostic')
        tool_after=payload_inventory(Path(tool).parent);parser_after=payload_inventory(parser.parent)
        require(tool_before==tool_after and parser_before==parser_after,'Actual complete tool/parser executable payload changed')
        tool_after_ref=write_new(output/'tool-payload-after.json',tool_after)
        parser_after_ref=write_new(output/'parser-payload-after.json',parser_after)
        deps=parser.with_suffix('.deps.json').read_bytes();(output/'parser-dependencies.json').write_bytes(deps)
        def reference(name):return dict(path=name,sha256=sha256_bytes((output/name).read_bytes()))
        provenance=dict(sourceSha=source,sourceTreeClean=clean,collectionKind='small-fixture-integration-not-owner-attribution',
            inputTraceSha256=record['traceSha256'],outputReportSha256=record['parsedSha256'],toolExecutable=tool,parserAssembly=str(parser),
            toolInventoryBefore=tool_before_ref,toolInventoryAfter=tool_after_ref,parserInventoryBefore=parser_before_ref,parserInventoryAfter=parser_after_ref,
            toolExecutableInventoryPath=Path(tool).name,parserAssemblyInventoryPath=parser.name,versionCommand=reference('tool-version.command.json'),
            parserCommand=reference('parse.command.json'),parserDependencies=reference('parser-dependencies.json'),parserDependenciesInventoryPath=parser.with_suffix('.deps.json').name,
            originalTraceOutputPath=str(trace),originalParserOutputDirectory=str(parsed),duration=None)
        record['executionProvenance']=write_new(output/'execution-provenance.json',provenance)
    except Exception as error:record['failure']=str(error);record['status']='failed'
    record['completedAtUtc']=datetime.now(timezone.utc).isoformat();write_new(output/'integration.json',record)
    return record

if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser(description=__doc__)
    for name in ('tool','fixture','parser','output','repository'):p.add_argument('--'+name,required=True)
    a=p.parse_args();r=integrate(a.tool,a.fixture,a.parser,a.output,a.repository);print(json.dumps(r));sys.exit(0 if r['status']=='diagnostic-pass' else 2)
