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

def run_capture(client_executable,request_path,output,sensor_executable,dll_path,*,host=False,require_foreground=True,timeout_seconds=600):
    output=Path(output).resolve();output.mkdir(parents=True,exist_ok=False)
    started=now()
    try:
        require(type(timeout_seconds) is int and 1<=timeout_seconds<=600,'Capture command timeout must remain within declared ten-minute bound')
        client=resolve_executable(client_executable);sensor=resolve_executable(sensor_executable)
        original=Path(request_path).read_bytes();request=parse_json_bytes(original)
        runtime=runtime_fingerprint(request['runtimeRoot']);source=request['commit']
        import re
        require(type(source) is str and re.fullmatch('[0-9a-f]{40}',source),'Exact capture source is required')
        request.update(outputRoot=str(output/'capture'),adapterBindingDirectory=str(output/'identity'))
        request_file=output/'actual-request.json';write_new(request_file,request)
        sensor_before=sha256_bytes(Path(sensor).read_bytes());client_before=sha256_bytes(Path(client).read_bytes())
        client_assembly_before=sha256_bytes(Path(client).with_suffix('.dll').read_bytes())
        version_raw,_=run_record([sensor,'--version'],output,'provider-version')
    except Exception as error:
        write_new(output/'attempt.json',dict(schemaVersion=1,kind='actual-baseline-capture-attempt',status='failed',phase='preflight',startedAtUtc=started,completedAtUtc=now(),failures=[str(error)],qualified=False))
        raise
    stop=threading.Event();ready=threading.Event();lock=threading.Lock();control={};failure=[];cleanup=[];child=None;status='failed';command_started=None;command_completed=None
    argv=[client,'--reference-host-fixture-request' if host else '--reference-capture-request',str(request_file)]
    with SensorJournal(output/'sensors') as journal:
        def sample():
            raw=b'';err=b'';begin=now();tick=time.perf_counter();query_exit=None;query_timeout=False
            query_argv=[sensor,'--query-gpu='+FIELDS,'--format=csv,noheader,nounits']
            try:
                with lock:
                    result=subprocess.run(query_argv,capture_output=True,timeout=10,shell=False,
                                          creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
                raw,err=result.stdout,result.stderr;query_exit=result.returncode;require(result.returncode==0,'Sensor query exit '+str(result.returncode))
                devices=parse_inventory(raw);power=power_status();window=window_facts(control.get('pid'),control.get('hwnd'))
                value=dict(observedAtUtc=now(),acquisitionStartedAtUtc=begin,elapsedSeconds=time.perf_counter()-tick,devices=devices,power=power,window=window)
                if require_foreground and control and window['status']!='controlled' and child is not None and child.poll() is None:value['failure']='Actual producer window foreground control failed'
            except subprocess.TimeoutExpired as error:
                raw,err=error.stdout or b'',error.stderr or b'';query_timeout=True
                value=dict(observedAtUtc=now(),acquisitionStartedAtUtc=begin,devices=[],failure='Sensor query timeout')
            except Exception as error:value=dict(observedAtUtc=now(),acquisitionStartedAtUtc=begin,devices=[],failure=str(error))
            value['command']=dict(argv=query_argv,exitCode=query_exit,timedOut=query_timeout,completedAtUtc=now())
            journal.append(value,raw,err)
            require(value.get('failure') is None,value.get('failure','Sensor failure'))
        def observe():
            try:
                sample();ready.set()
                while not stop.wait(1):sample()
            except Exception as error:failure.append(str(error));journal.failed=str(error);journal._snapshot();ready.set()
        observer=threading.Thread(target=observe,name='baseline-physical-observer',daemon=True);observer.start()
        try:
            require(ready.wait(15) and not failure,'Initial physical sensor observation unavailable')
            with (output/'stdout.log').open('xb') as stdout,(output/'stderr.log').open('xb') as stderr:
                command_started=now();deadline=time.monotonic()+timeout_seconds
                child=subprocess.Popen(argv,stdout=stdout,stderr=stderr,shell=False)
                checkpoint=output/'identity'/'checkpoint.json'
                while not checkpoint.exists():
                    require(child.poll() is None,'Producer exited before actual adapter checkpoint')
                    require(time.monotonic()<deadline and not failure,'Capture checkpoint deadline/sensor failure')
                    time.sleep(.01)
                actual=parse_json_bytes(checkpoint.read_bytes())
                context=dict(processId=child.pid,sourceSha=source,runtimeFingerprint=runtime.upper(),clientAssemblySha256=client_assembly_before.upper(),
                             startedAtUtc=command_started,argv=argv,requestSha256=sha256_bytes(request_file.read_bytes()))
                def control_before_release(_proof):
                    control.update(pid=child.pid,hwnd=actual['nativeWindowHandle'])
                    facts=window_facts(child.pid,actual['nativeWindowHandle'])
                    write_new(output/'pre-window-control.json',facts)
                    require(not require_foreground or facts['status']=='controlled','Actual child is not foreground-controlled before window release')
                with lock:binding=complete_checkpoint(checkpoint.parent,context,dll_path,sensor,control_before_release)
                while child.poll() is None:
                    require(time.monotonic()<deadline,'Declared capture command deadline exceeded')
                    require(not failure,'Physical observer/control failed: '+str(failure))
                    time.sleep(.01)
                command_completed=now()
                exit_code=child.returncode;control.clear();require(exit_code==0,'Actual capture command failed with exit '+str(exit_code))
                manifest_path=output/'capture'/('fixture-manifest.json' if host else 'manifest.json')
                require(manifest_path.is_file() and not (manifest_path.parent/'failure.json').exists(),'Producer did not retain a complete successful manifest')
                manifest=parse_json_bytes(manifest_path.read_bytes())
                require(manifest.get('status')=='structural-pass' and manifest.get('sourceSha' if host else 'commit')==source and str(manifest.get('runtimeFingerprint','')).lower()==runtime,'Final producer source/runtime/status mismatch')
                final_identity=manifest.get('fixture',{}).get('readback',{}).get('adapterIdentity') if host else manifest.get('adapterIdentity')
                require(final_identity==actual.get('adapterIdentity'),'Final producer identity differs from exact live checkpoint')
                if host:
                    owner=manifest_path.parent/'owner.json'
                    require(owner.is_file() and sha256_bytes(owner.read_bytes())==str(manifest.get('ownerSha256','')).lower(),'Final owner stream missing or changed')
                else:require(type(manifest.get('captures')) is list and manifest['captures'],'Native producer has no actual captures')
                stop.set();observer.join(15);require(not observer.is_alive(),'Sensor observer did not stop within its query bound')
                sample();require(not failure,'Physical observation failed')
                require(runtime_fingerprint(request['runtimeRoot'])==runtime,'Sealed runtime assets changed during capture')
                require(sha256_bytes(Path(sensor).read_bytes())==sensor_before and sha256_bytes(Path(client).read_bytes())==client_before and sha256_bytes(Path(client).with_suffix('.dll').read_bytes())==client_assembly_before,'Capture/provider executable or actual client assembly changed')
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
            stop.set();observer.join(15)
            if observer.is_alive():failure.append('Observer query did not finish within bound')
            record=dict(schemaVersion=1,kind='actual-baseline-capture-attempt',status=status,sourceSha=source,runtimeFingerprint=runtime,
                        startedAtUtc=started,completedAtUtc=now(),commandStartedAtUtc=command_started,commandCompletedAtUtc=command_completed,argv=argv,processId=child.pid if child else None,exitCode=child.returncode if child else None,
                        foregroundEnforced=require_foreground,failures=failure,cleanup=cleanup,requestSha256=sha256_bytes(request_file.read_bytes()),originalRequestSha256=sha256_bytes(original),
                        clientExecutableSha256=client_before,clientAssemblySha256=client_assembly_before,sensorExecutableSha256=sensor_before,providerVersionSha256=sha256_bytes(version_raw),
                        physicalAcceptance='unreviewed evidence; requires accepted source/contract/policies and complete baseline controls',qualified=False)
            write_new(output/'attempt.json',record)
    require(status=='structural-pass','Capture failed; complete immutable attempt retained')
    return record
