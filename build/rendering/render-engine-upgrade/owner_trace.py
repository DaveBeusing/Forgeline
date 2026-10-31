"""Separate bounded real-render-owner sampled attach supervisor; never qualification timing."""
from __future__ import annotations
import json,os,subprocess,threading,time,hashlib,shutil
from pathlib import Path
from datetime import datetime,timezone,timedelta
from baseline_contracts import require,read_json,parse_json_bytes,write_new,sha256_bytes,utc
from provider import resolve_executable,run_record
from adapter_identity import observe_process
from capture_runner import payload_inventory,run_capture
from diagnostic_control import atomic_new

def now():return datetime.now(timezone.utc).isoformat()
def hash_file(path):
    digest=hashlib.sha256()
    with Path(path).open('rb') as stream:
        for chunk in iter(lambda:stream.read(1024*1024),b''):digest.update(chunk)
    return digest.hexdigest()
def ref(root,path):return dict(path=Path(path).relative_to(root).as_posix(),sha256=hash_file(path))

def terminate_bounded(child):
    cleanup=[]
    if child.poll() is None:
        try:child.terminate();cleanup.append('terminate-requested')
        except OSError as error:cleanup.append('terminate-error: '+str(error))
        try:child.wait(timeout=5)
        except (subprocess.TimeoutExpired,OSError) as error:cleanup.append('terminate-wait: '+str(error))
    if child.poll() is None:
        try:child.kill();cleanup.append('kill-requested')
        except OSError as error:cleanup.append('kill-error: '+str(error))
        try:child.wait(timeout=5)
        except (subprocess.TimeoutExpired,OSError) as error:cleanup.append('kill-wait: '+str(error))
    if child.poll() is None:cleanup.append('tracer-still-alive-after-bounded-cleanup')
    return cleanup

class OwnerTraceSession:
    def __init__(self,tool,root,control,duration,max_bytes):
        self.tool,self.root,self.control,self.duration,self.max_bytes=tool,root,control,duration,max_bytes
        self.child=None;self.worker=None;self.failure=None;self.context=None;self.cleanup=[]
        self.trace=root/'owner.nettrace';self.command=None;self.stdout=None;self.stderr=None
    def begin(self,context):
        self.context=context;checkpoint=context['checkpoint'];pid=context['processId']
        owner=checkpoint.get('ownerThread',{})
        require(type(owner.get('nativeThreadId')) is int and owner['nativeThreadId']>0 and owner.get('processId')==pid,'Actual render-owner native thread is missing from checkpoint')
        self.control.mkdir(exist_ok=False)
        atomic_new(self.control/'trace-control.json',dict(schemaVersion=1,runId=checkpoint['runId'],processId=pid,
            sourceSha=checkpoint['sourceSha'],checkpointSha256=sha256_bytes(context['checkpointBytes'])))
        # Arm before binding release; attach only after the producer's existing initialization.
        self.worker=threading.Thread(target=self._await_ready,name='bounded-owner-trace',daemon=True);self.worker.start()
    def _await_ready(self):
        try:
            deadline=self.context['commandDeadline']
            path=self.control/'trace-ready.json'
            while not path.exists():
                require(self.context['stillAlive']() and time.monotonic()<deadline,'Actual producer did not reach pre-window trace readiness within original command bound')
                time.sleep(.01)
            require(path.stat().st_size<=65536 and not path.is_symlink(),'Producer trace readiness exceeds declared bound')
            self.ready_bytes=path.read_bytes();ready=parse_json_bytes(self.ready_bytes);checkpoint=self.context['checkpoint']
            require(ready.get('kind')=='actual-owner-trace-ready' and ready.get('qualified') is False and ready.get('runId')==checkpoint['runId'] and ready.get('processId')==self.context['processId'] and ready.get('sourceSha')==checkpoint['sourceSha'] and ready.get('checkpointSha256','').lower()==sha256_bytes(self.context['checkpointBytes']) and ready.get('ownerThread')==checkpoint['ownerThread'],'Producer trace readiness belongs to another source/process/native owner/session')
            self._start_collect()
        except Exception as error:
            self.failure=str(error)
            try:
                if self.child is not None:self.cleanup.extend(terminate_bounded(self.child))
                self._finish_record()
            finally:self.context['fail']('Separate owner attach readiness failed: '+str(error))
    def _start_collect(self):
        context=self.context;checkpoint=context['checkpoint'];pid=context['processId'];owner=checkpoint['ownerThread']
        before=observe_process(pid);write_new(self.root/'process-before.json',before)
        require(before.get('alive') is True and before['creationFileTimeUtc']==checkpoint['adapterIdentity']['processStartedFileTimeUtc'],'Actual renderer lifetime unavailable before attach')
        argv=[self.tool,'collect','--process-id',str(pid),'--profile','gc-verbose','--duration',self.duration,'--buffersize','256','--output',str(self.trace)]
        self.command=dict(argv=argv,startedAtUtc=now(),exitCode=None,failure=None,outputs={})
        self.stdout=(self.root/'collect.stdout').open('xb');self.stderr=(self.root/'collect.stderr').open('xb')
        try:
            self.child=subprocess.Popen(argv,stdout=self.stdout,stderr=self.stderr,shell=False,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0,env=dict(os.environ,DOTNET_CLI_UI_LANGUAGE='en-US'))
            # Pinned source prints Output File only AFTER StartEventPipeSessionAsync succeeds.
            # It precedes CopyToAsync; eventual real in-window samples remain mandatory evidence.
            ready_deadline=min(time.monotonic()+10,context['commandDeadline'])
            expected=str(self.trace)
            while True:
                require(self.child.poll() is None and context['stillAlive'](),'Tracer/actual producer exited before session-established readiness')
                try:lines=(self.root/'collect.stdout').read_text(encoding='utf-8-sig',errors='replace').splitlines()
                except OSError:lines=[]
                ready=any(line.strip().startswith('Output File') and ':' in line and line.split(':',1)[1].strip()==expected for line in lines)
                if ready:break
                require(time.monotonic()<ready_deadline,'Pinned trace session did not establish within remaining original checkpoint budget')
                time.sleep(.01)
            write_new(self.root/'attach-ready.json',dict(schemaVersion=1,qualified=False,processId=pid,ownerThread=owner,
                checkpointSha256=sha256_bytes(context['checkpointBytes']),observedAtUtc=now(),readiness='Pinned tool Output File line after successful session creation; useful warm coverage still unverified'))
            atomic_new(self.control/'trace-attached.json',dict(sessionEstablished=True,runId=checkpoint['runId'],processId=pid,readySha256=sha256_bytes(self.ready_bytes),createdAtUtc=now()))
            self._collect()
        except Exception as error:
            self.failure=str(error)
            if self.child is not None:self.cleanup.extend(terminate_bounded(self.child))
            self._finish_record();raise
    def _collect(self):
        try:
            deadline=min(time.monotonic()+int(self.duration[-2:])+10,self.context['commandDeadline'])
            while self.child.poll() is None:
                require(time.monotonic()<deadline,'Bounded attach command exceeded declared duration plus startup/flush allowance')
                require(not self.trace.exists() or self.trace.stat().st_size<=self.max_bytes,'Declared raw trace byte bound exceeded; partial trace retained')
                time.sleep(.05)
            require(self.child.returncode==0,'Actual pinned attach command failed')
            self._finish_record()
            require(self.trace.is_file() and 0<self.trace.stat().st_size<=self.max_bytes,'Actual bounded raw trace is missing')
            after=observe_process(self.context['processId']);write_new(self.root/'process-after.json',after)
            require(after.get('alive') is True,'Actual renderer exited before post-trace OS observation')
            atomic_new(self.control/'trace-release.json',dict(completed=True,runId=self.context['checkpoint']['runId'],
                processId=self.context['processId'],checkpointSha256=sha256_bytes(self.context['checkpointBytes']),traceSha256=hash_file(self.trace),completedAtUtc=now()))
        except Exception as error:
            self.failure=str(error)
            if self.child is not None:self.cleanup.extend(terminate_bounded(self.child))
            try:
                if self.command is not None and not (self.root/'collect.command.json').exists():self._finish_record()
            finally:self.context['fail']('Separate owner attach failed: '+str(error))
    def _finish_record(self):
        if self.command is None or (self.root/'collect.command.json').exists():return
        for stream in (self.stdout,self.stderr):
            if stream is not None and not stream.closed:stream.close()
        self.command.update(completedAtUtc=now(),exitCode=self.child.returncode if self.child is not None else None,failure=self.failure,cleanup=self.cleanup)
        for name in ('stdout','stderr'):
            path=self.root/('collect.'+name);self.command['outputs'][name]=dict(path=path.name,sha256=hash_file(path))
        write_new(self.root/'collect.command.json',self.command)
    def close(self):
        if self.worker is not None:self.worker.join(timeout=45)
        if self.child is not None and self.child.poll() is None:
            self.failure=self.failure or 'Supervisor cleanup interrupted active tracer';self.cleanup.extend(terminate_bounded(self.child))
        if self.worker is not None:
            self.worker.join(timeout=10);require(not self.worker.is_alive(),'Trace monitor did not complete bounded cleanup')
        self._finish_record()
        require(self.child is None or self.child.poll() is not None,'Actual tracer remains alive after bounded cleanup; retained outcomes require external intervention')

def profile_owner(client,request,sensor,cuda_driver,tool,parser,repository,output,*,setup_hold=False,duration_seconds=30,max_trace_bytes=512*1024*1024):
    output=Path(output).resolve()
    for candidate in (client,tool,parser):
        path=Path(candidate)
        if path.is_file():require(not output.is_relative_to(path.resolve(strict=True).parent),'Diagnostic output cannot mutate an executable payload')
    output.mkdir(parents=True,exist_ok=False);record=dict(schemaVersion=1,kind='actual-owner-trace-attempt',qualified=False,status='failed',startedAtUtc=now());session=None
    try:
        require(type(duration_seconds) is int and 1<=duration_seconds<=30,'Actual attach duration must remain within1..30seconds')
        require(type(max_trace_bytes) is int and 1024*1024<=max_trace_bytes<=4*1024*1024*1024,'Declared trace storage bound outside1MiB..4GiB')
        source=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repository,text=True).strip()
        require(not subprocess.check_output(['git','status','--porcelain','--untracked-files=normal'],cwd=repository).strip(),'Owner attach requires clean committed tooling/producer source')
        record.update(sourceSha=source,sourceTreeClean=True)
        tool=resolve_executable(tool);client=resolve_executable(client);parser=Path(parser).resolve(strict=True)
        require(all(not output.is_relative_to(root) for root in (Path(tool).parent,Path(client).parent,parser.parent)),'Diagnostic output cannot mutate an executable payload')
        req=read_json(request);require(req.get('commit')==source and req.get('warmups')==64 and req.get('samples')==512 and req.get('runProfile','Warm')=='Warm' and req.get('collectorMode','Full')=='Full','Actual owner diagnostic needs exact source/Full64/512 warm recipe')
        require(req.get('captureStageDiagnostics') is not True,'Profiled diagnostic must keep stage copies off')
        reserve=(max_trace_bytes*8+128*1024*1024)*3//2;free=shutil.disk_usage(output).free
        write_new(output/'capacity.json',dict(requiredBytes=reserve,freeBytes=free,maxTraceBytes=max_trace_bytes,reserveFactor=1.5,qualified=False));require(free>=reserve,'Insufficient bounded trace/ETLX capacity')
        tools_before=payload_inventory(Path(tool).parent);parsers_before=payload_inventory(parser.parent)
        tool_before_ref=write_new(output/'tool-payload-before.json',tools_before);parser_before_ref=write_new(output/'parser-payload-before.json',parsers_before)
        version,_=run_record([tool,'--version'],output,'tool-version');require(version.decode(errors='replace').strip().startswith('10.0.750501'),'Pinned actual dotnet-trace version unavailable')
        profiles,_=run_record([tool,'list-profiles'],output,'tool-profiles');require(b'gc-verbose' in profiles,'Pinned actual gc-verbose profile unavailable')
        identity_raw,_=run_record(['dotnet',str(parser),'--identity'],output,'parser-identity')
        identity=parse_json_bytes(identity_raw)
        require(type(identity.get('schemaVersion')) is int and identity['schemaVersion']==1 and identity.get('kind')=='allocation-parser-identity' and str(identity.get('buildIdentity','')).lower().endswith('+'+source) and identity.get('assemblySha256')==hash_file(parser),'Actual parser source/image identity is stale or unverified before launch')
        dependencies=parser.with_suffix('.deps.json');deps_raw=dependencies.read_bytes()
        require(identity.get('dependenciesSha256')==sha256_bytes(deps_raw) and 'Microsoft.Diagnostics.Tracing.TraceEvent/4.0.0' in parse_json_bytes(deps_raw).get('libraries',{}),'Actual parser dependency identity is not the pinned build')
        duration=f'00:00:{duration_seconds:02}';session=OwnerTraceSession(tool,output,output/'trace-control',duration,max_trace_bytes)
        req['ownerTraceControlDirectory']=str(session.control);actual_request=output/'profiled-request.json';write_new(actual_request,req)
        run_capture(client,actual_request,output/'producer',sensor,cuda_driver,host=True,diagnostic_only=True,diagnostic_setup_hold=setup_hold,diagnostic_before_release=session.begin)
        session.close();require(session.failure is None,'Separate actual owner attach failed: '+str(session.failure))
        parsed=output/'parsed';run_record(['dotnet',str(parser),str(session.trace),str(parsed),str(session.context['processId'])],output,'parse',timeout=120)
        report=read_json(parsed/'allocation-stacks.json');require(report.get('status')=='diagnostic-pass','Actual sampled owner trace parsing unavailable')
        tools_after=payload_inventory(Path(tool).parent);parsers_after=payload_inventory(parser.parent)
        require(tools_before==tools_after and parsers_before==parsers_after,'Actual tool/parser payload changed during attach/parse')
        tool_after_ref=write_new(output/'tool-payload-after.json',tools_after);parser_after_ref=write_new(output/'parser-payload-after.json',parsers_after)
        dependencies=parser.with_suffix('.deps.json');(output/'parser-dependencies.json').write_bytes(dependencies.read_bytes())
        provenance=dict(sourceSha=source,sourceTreeClean=True,collectionKind='actual-host-owner-separate-profiled-diagnostic',duration=duration,
            inputTraceSha256=hash_file(session.trace),outputReportSha256=hash_file(parsed/'allocation-stacks.json'),toolExecutable=tool,parserAssembly=str(parser),
            toolInventoryBefore=tool_before_ref,toolInventoryAfter=tool_after_ref,parserInventoryBefore=parser_before_ref,parserInventoryAfter=parser_after_ref,
            toolExecutableInventoryPath=Path(tool).name,parserAssemblyInventoryPath=parser.name,versionCommand=ref(output,output/'tool-version.command.json'),
            parserCommand=ref(output,output/'parse.command.json'),parserIdentityCommand=ref(output,output/'parser-identity.command.json'),parserDependencies=ref(output,output/'parser-dependencies.json'),parserDependenciesInventoryPath=dependencies.name,
            originalTraceOutputPath=str(session.trace),originalParserOutputDirectory=str(parsed))
        provenance_ref=write_new(output/'execution-provenance.json',provenance)
        owner_path=output/'producer/capture/owner.json';owner=read_json(owner_path)
        require(owner['ownerThread']==session.context['checkpoint']['ownerThread'],'Final actual native render-owner differs from attach checkpoint')
        wrapper=dict(schemaVersion=1,kind='sampled-owner-allocation-diagnostic',qualified=False,report=ref(output,parsed/'allocation-stacks.json'),owner=ref(output,owner_path),
            processBefore=ref(output,output/'process-before.json'),processAfter=ref(output,output/'process-after.json'),command=ref(output,output/'collect.command.json'),trace=ref(output,session.trace),executionProvenance=provenance_ref)
        wrapper_ref=write_new(output/'owner-diagnostic.json',wrapper)
        from baseline_admission import validate_sampled_owner_diagnostic
        result=validate_sampled_owner_diagnostic(output,wrapper_ref,source,owner['runtimeFingerprint'].lower(),hash_file(client))
        record.update(status='diagnostic-pass',sourceSha=source,ownerDiagnostic=wrapper_ref,analysis=result)
    except Exception as error:record.update(failure=str(error),attribution='unavailable; raw records retained, not promoted to verified warm owner sites')
    finally:
        if session is not None:
            try:session.close()
            except Exception as error:record.setdefault('cleanupFailures',[]).append(str(error));record['status']='failed'
        record['completedAtUtc']=now();write_new(output/'attempt.json',record)
    return record

if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser(description=__doc__)
    for name in ('client','request','sensor','cuda-driver','tool','parser','repository','output'):p.add_argument('--'+name,required=True)
    p.add_argument('--setup-hold',action='store_true');p.add_argument('--duration-seconds',type=int,default=30)
    a=p.parse_args();r=profile_owner(a.client,a.request,a.sensor,a.cuda_driver,a.tool,a.parser,a.repository,a.output,setup_hold=a.setup_hold,duration_seconds=a.duration_seconds);print(json.dumps(r));raise SystemExit(0 if r['status']=='diagnostic-pass' else 2)
