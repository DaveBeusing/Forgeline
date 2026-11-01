"""Bounded real-process fixture launcher; no implicit physical/performance acceptance."""
from __future__ import annotations
import json,os,subprocess,threading,time
from datetime import datetime,timezone
from pathlib import Path
from adapter_handshake import complete_checkpoint
from baseline_contracts import parse_json_bytes,require,sha256_bytes,write_new
from provider import FIELDS,parse_inventory,power_status,resolve_executable,run_record,window_facts
from sensor_journal import SensorJournal

def now():return datetime.now(timezone.utc).isoformat()
def runtime_fingerprint(root):
    import hashlib
    root=Path(root).resolve(strict=True);result=hashlib.sha256()
    for path in sorted(root.rglob('*'),key=lambda p:str(p).encode('utf-16-be')):
        require(not path.is_symlink(),'Sealed runtime cannot contain symbolic links')
        if path.is_file():result.update(path.relative_to(root).as_posix().encode('utf-8'));result.update(bytes.fromhex(sha256_bytes(path.read_bytes())))
    return result.hexdigest()

def payload_inventory(root):
    root=Path(root).resolve(strict=True);rows=[]
    import hashlib
    for path in sorted(root.rglob('*'),key=lambda p:p.relative_to(root).as_posix().encode('utf-16-be')):
        require(not path.is_symlink(),'Executable payload cannot contain symbolic links')
        if path.is_file():
            digest=hashlib.sha256()
            with path.open('rb') as stream:
                for block in iter(lambda:stream.read(1024*1024),b''):digest.update(block)
            rows.append(dict(path=path.relative_to(root).as_posix(),bytes=path.stat().st_size,sha256=digest.hexdigest()))
    require(rows,'Executable payload inventory is empty');return rows

def run_capture(client_executable,request_path,output,sensor_executable,dll_path,*,host=False,gameplay=False,require_foreground=True,timeout_seconds=600,
                diagnostic_only=False,diagnostic_setup_hold=False,diagnostic_before_release=None):
    output=Path(output).resolve()
    # Reject a known executable payload target BEFORE creating even an empty evidence directory.
    candidate=Path(client_executable)
    if candidate.is_file():
        require(not output.is_relative_to(candidate.resolve(strict=True).parent),'Evidence output cannot mutate the executable payload directory')
    output.mkdir(parents=True,exist_ok=False)
    started=now()
    try:
        require(not (host and gameplay),'Gameplay is an actual session mode, not an authored host fixture')
        require(type(timeout_seconds) is int and 1<=timeout_seconds<=600,'Capture command timeout must remain within declared ten-minute bound')
        client=resolve_executable(client_executable);sensor=resolve_executable(sensor_executable)
        require(not output.is_relative_to(Path(client).parent),'Evidence output cannot mutate the executable payload directory')
        payload_before=payload_inventory(Path(client).parent);write_new(output/'payload-inventory-before.json',payload_before)
        original=Path(request_path).read_bytes();request=parse_json_bytes(original)
        require(type(diagnostic_only) is bool and type(diagnostic_setup_hold) is bool,'Diagnostic mode flags must be actual booleans')
        diagnostic_only=diagnostic_only or request.get('captureStageDiagnostics') is True or request.get('ownerTraceControlDirectory') is not None or request.get('captureBoundInputProvenanceVersion') is not None
        require(not diagnostic_setup_hold or diagnostic_only,'External setup hold is diagnostic-only')
        require(diagnostic_before_release is None or diagnostic_only and callable(diagnostic_before_release),'Attach callback is diagnostic-only')
        runtime=runtime_fingerprint(request['runtimeRoot']);source=request['commit']
        if gameplay:require(request.get('warmups')==64 and request.get('samples')==512,'Actual gameplay uses unchanged64/512 declared window')
        import re
        require(type(source) is str and re.fullmatch('[0-9a-f]{40}',source),'Exact capture source is required')
        observation_version=request.get('ownerObservationVersion',0)
        require(type(observation_version) is int and observation_version in (0,1),'Unsupported owner-observation contract version')
        if observation_version==1:request['postWindowControlDirectory']=str(output/'post-window')
        request.update(outputRoot=str(output/'capture'),adapterBindingDirectory=str(output/'identity'))
        request_file=output/'actual-request.json';write_new(request_file,request)
        sensor_before=sha256_bytes(Path(sensor).read_bytes());client_before=sha256_bytes(Path(client).read_bytes())
        client_assembly_before=sha256_bytes(Path(client).with_suffix('.dll').read_bytes())
        version_raw,_=run_record([sensor,'--version'],output,'provider-version')
    except Exception as error:
        write_new(output/'attempt.json',dict(schemaVersion=1,kind='actual-baseline-capture-attempt',diagnosticOnly=diagnostic_only,status='failed',phase='preflight',startedAtUtc=started,completedAtUtc=now(),failures=[str(error)],qualified=False))
        raise
    wake=threading.Event();post_control=None
    stop=threading.Event();ready=threading.Event();lock=threading.Lock();control={};failure=[];cleanup=[];child=None;status='failed';command_started=None;command_completed=None
    argv=[client,'--smoke-test'] if gameplay else [client,'--reference-host-fixture-request' if host else '--reference-capture-request',str(request_file)]
    child_env=None
    if gameplay:
        child_env=dict(os.environ)
        child_env.update(FORGELINE_RENDER_TELEMETRY_OUTPUT=str(output/'capture'/'owner.json'),FORGELINE_RENDER_TELEMETRY_CONTEXT='',
            FORGELINE_RENDER_TELEMETRY_SAMPLES='512',FORGELINE_RENDER_TELEMETRY_WARMUPS='64',FORGELINE_RENDER_TELEMETRY_ADAPTER_BINDING=str(output/'identity'))
        if observation_version==1:
            child_env.update(FORGELINE_RENDER_OWNER_OBSERVATION_VERSION='1',FORGELINE_RENDER_OWNER_CYCLE_CAPACITY=str(request.get('ownerCycleCapacity',8192)),
                FORGELINE_RENDER_POST_WINDOW_CONTROL=str(output/'post-window'))
    with SensorJournal(output/'sensors') as journal:
        def sample():
            requested_ready=post_control.begin_query() if post_control is not None else None
            raw=b'';err=b'';begin=now();tick=time.perf_counter();query_exit=None;query_timeout=False
            query_argv=[sensor,'--query-gpu='+FIELDS,'--format=csv,noheader,nounits']
            try:
                with lock:
                    begin=now()
                    result=subprocess.run(query_argv,capture_output=True,timeout=10,shell=False,
                                          creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
                raw,err=result.stdout,result.stderr;query_exit=result.returncode;require(result.returncode==0,'Sensor query exit '+str(result.returncode))
                devices=parse_inventory(raw);power=power_status();phase=dict(control);window=window_facts(phase.get('pid'),phase.get('hwnd'))
                value=dict(observedAtUtc=now(),acquisitionStartedAtUtc=begin,elapsedSeconds=time.perf_counter()-tick,devices=devices,power=power,window=window,controlPhase='diagnostic-setup' if phase and phase.get('enforce') is False else 'controlled' if phase else 'startup-or-after-exit')
                if require_foreground and phase and phase.get('enforce',True) and window['status']!='controlled' and child is not None and child.poll() is None:value['failure']='Actual producer window foreground control failed'
            except subprocess.TimeoutExpired as error:
                raw,err=error.stdout or b'',error.stderr or b'';query_timeout=True
                value=dict(observedAtUtc=now(),acquisitionStartedAtUtc=begin,devices=[],failure='Sensor query timeout')
            except Exception as error:value=dict(observedAtUtc=now(),acquisitionStartedAtUtc=begin,devices=[],failure=str(error))
            value['command']=dict(argv=query_argv,exitCode=query_exit,timedOut=query_timeout,completedAtUtc=now())
            if requested_ready is not None:value['postWindowReadySha256']=sha256_bytes(requested_ready)
            receipt=journal.append(value,raw,err)
            if post_control is not None:post_control.finish_query(requested_ready,receipt)
            require(value.get('failure') is None,value.get('failure','Sensor failure'))
        def observe():
            try:
                sample();ready.set()
                while not stop.is_set():
                    wake.wait(1);wake.clear()
                    if not stop.is_set():sample()
            except Exception as error:failure.append(str(error));journal.failed=str(error);journal._snapshot();ready.set()
        observer=threading.Thread(target=observe,name='baseline-physical-observer',daemon=True);observer.start()
        try:
            require(ready.wait(15) and not failure,'Initial physical sensor observation unavailable')
            with (output/'stdout.log').open('xb') as stdout,(output/'stderr.log').open('xb') as stderr:
                command_started=now();deadline=time.monotonic()+timeout_seconds
                child=subprocess.Popen(argv,stdout=stdout,stderr=stderr,shell=False,**(dict(env=child_env) if child_env is not None else {}))
                checkpoint=output/'identity'/'checkpoint.json'
                while not checkpoint.exists():
                    require(child.poll() is None,'Producer exited before actual adapter checkpoint')
                    require(time.monotonic()<deadline and not failure,'Capture checkpoint deadline/sensor failure')
                    time.sleep(.01)
                actual=parse_json_bytes(checkpoint.read_bytes())
                context=dict(processId=child.pid,sourceSha=source,runtimeFingerprint=runtime.upper(),clientAssemblySha256=client_assembly_before.upper(),
                             startedAtUtc=command_started,timeoutSeconds=timeout_seconds,argv=argv,requestSha256=sha256_bytes(request_file.read_bytes()))
                def control_before_release(_proof):
                    # Startup is not a controlled interval. Do not invalidate the observer while the
                    # explicit external operator handoff waits within the original producer budget.
                    if diagnostic_setup_hold:
                        control.update(pid=child.pid,hwnd=actual['nativeWindowHandle'],enforce=False)
                        from diagnostic_control import hold_for_operator
                        hold_for_operator(output,checkpoint.read_bytes(),lambda: child.poll() is None and time.monotonic()<deadline and not failure)
                    if diagnostic_before_release is not None:
                        diagnostic_before_release(dict(checkpoint=actual,checkpointBytes=checkpoint.read_bytes(),commandContext=context,
                                                       binding=_proof,processId=child.pid,commandDeadline=deadline,fail=failure.append,stillAlive=lambda: child.poll() is None))
                    control.update(pid=child.pid,hwnd=actual['nativeWindowHandle'],enforce=True)
                    facts=window_facts(child.pid,actual['nativeWindowHandle'])
                    write_new(output/'pre-window-control.json',facts)
                    require(not require_foreground or facts['status']=='controlled','Actual child is not foreground-controlled before window release')
                binding=complete_checkpoint(checkpoint.parent,context,dll_path,sensor,control_before_release,query_lock=lock)
                if observation_version==1:
                    from post_window_control import PostWindowSupervisor
                    post_control=PostWindowSupervisor(output/'post-window',checkpoint.read_bytes(),binding,deadline,lambda: child.poll() is None and not failure,wake)
                while child.poll() is None:
                    require(time.monotonic()<deadline,'Declared capture command deadline exceeded')
                    require(not failure,'Physical observer/control failed: '+str(failure))
                    if post_control is not None:post_control.poll()
                    time.sleep(.01)
                command_completed=now()
                exit_code=child.returncode
                require(post_control is None or post_control.completed,'Producer exited without an ordinary fresh-query durable ACK')
                control.clear();require(exit_code==0,'Actual capture command failed with exit '+str(exit_code))
                manifest_path=output/'capture'/('owner.json' if gameplay else 'fixture-manifest.json' if host else 'manifest.json')
                require(manifest_path.is_file() and not (manifest_path.parent/'failure.json').exists(),'Producer did not retain a complete successful manifest')
                manifest=parse_json_bytes(manifest_path.read_bytes())
                if gameplay:
                    require(manifest.get('window',{}).get('completed') is True and manifest.get('overflowed') is False and manifest.get('failedFixtureAttempt') is None and
                        manifest.get('producerMode')=='actual-live-gameplay' and manifest.get('modeAttestationAvailable') is True and manifest.get('sourceSha')==source and str(manifest.get('runtimeFingerprint','')).lower()==runtime,
                        'Actual gameplay session/progress/window/source/runtime proof missing')
                else:require(manifest.get('status')=='structural-pass' and manifest.get('sourceSha' if host else 'commit')==source and str(manifest.get('runtimeFingerprint','')).lower()==runtime,'Final producer source/runtime/status mismatch')
                final_identity=manifest.get('fixture',{}).get('readback',{}).get('adapterIdentity') if host and not gameplay else manifest.get('adapterIdentity')
                require(final_identity==actual.get('adapterIdentity'),'Final producer identity differs from exact live checkpoint')
                if host:
                    if request.get('collectorMode','Full')!='CollectorOff':
                        owner=manifest_path.parent/'owner.json'
                        require(owner.is_file() and sha256_bytes(owner.read_bytes())==str(manifest.get('ownerSha256','')).lower(),'Final owner stream missing or changed')
                    else:require(manifest.get('owner') is None and manifest.get('fixture',{}).get('collectorControl',{}).get('collectorEnabled') is False,'OFF must retain a real separate control probe and no fake owner')
                elif not gameplay:require(type(manifest.get('captures')) is list and manifest['captures'],'Native producer has no actual captures')
                stop.set();wake.set();observer.join(15);require(not observer.is_alive(),'Sensor observer did not stop within its query bound')
                sample();require(not failure,'Physical observation failed')
                require(runtime_fingerprint(request['runtimeRoot'])==runtime,'Sealed runtime assets changed during capture')
                require(sha256_bytes(Path(sensor).read_bytes())==sensor_before and sha256_bytes(Path(client).read_bytes())==client_before and sha256_bytes(Path(client).with_suffix('.dll').read_bytes())==client_assembly_before,'Capture/provider executable or actual client assembly changed')
                payload_after=payload_inventory(Path(client).parent);write_new(output/'payload-inventory-after.json',payload_after)
                require(payload_before==payload_after,'Complete executable payload changed during capture')
                status='structural-pass'
        except Exception as error:
            failure.append(str(error));control.clear()
            if child is not None:
                if child.poll() is None:
                    try:child.terminate();cleanup.append('terminate-requested')
                    except OSError as cleanup_error:cleanup.append('terminate-error: '+str(cleanup_error))
                    try:child.wait(timeout=15)
                    except (subprocess.TimeoutExpired,OSError) as cleanup_error:
                        cleanup.append('terminate-wait-error: '+str(cleanup_error))
                        try:child.kill();cleanup.append('kill-requested');child.wait(timeout=15)
                        except (subprocess.TimeoutExpired,OSError) as kill_error:cleanup.append('kill-wait-error: '+str(kill_error))
                if child.poll() is not None:
                    if command_completed is None:command_completed=now()
                else:cleanup.append('producer-still-alive-after-bounded-cleanup')
        finally:
            stop.set();wake.set();observer.join(15)
            if observer.is_alive():failure.append('Observer query did not finish within bound')
            record=dict(schemaVersion=1,kind='actual-baseline-capture-attempt',diagnosticOnly=diagnostic_only,status=status,sourceSha=source,runtimeFingerprint=runtime,
                        startedAtUtc=started,completedAtUtc=now(),commandStartedAtUtc=command_started,commandCompletedAtUtc=command_completed,argv=argv,processId=child.pid if child else None,exitCode=child.returncode if child else None,
                        captureMode='actual-live-gameplay' if gameplay else 'reference-host-fixture' if host else 'native-reference',
                        postWindowControl=post_control.evidence() if post_control is not None else None,foregroundEnforced=require_foreground,failures=failure,cleanup=cleanup,requestSha256=sha256_bytes(request_file.read_bytes()),originalRequestSha256=sha256_bytes(original),
                        payloadInventoryBeforeSha256=sha256_bytes((output/'payload-inventory-before.json').read_bytes()),
                        payloadInventoryAfterSha256=sha256_bytes((output/'payload-inventory-after.json').read_bytes()) if (output/'payload-inventory-after.json').is_file() else None,
                        clientExecutableSha256=client_before,clientAssemblySha256=client_assembly_before,sensorExecutableSha256=sensor_before,providerVersionSha256=sha256_bytes(version_raw),
                        physicalAcceptance='unreviewed evidence; requires accepted source/contract/policies and complete baseline controls',qualified=False)
            write_new(output/'attempt.json',record)
    require(status=='structural-pass','Capture failed; complete immutable attempt retained')
    return record
