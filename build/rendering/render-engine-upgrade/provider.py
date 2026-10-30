"""Read-only physical diagnostics. This provider cannot publish qualified wave0 input."""
from __future__ import annotations
import csv
import ctypes
from datetime import datetime,timezone
import io
import json
import os
from pathlib import Path
import subprocess
import time
from baseline_contracts import (ContractError,DiagnosticSeries,atomic_diagnostic_snapshot,candidate_mapping,event_reasons,numeric,require,sha256_bytes,write_new,utc)
FIELDS='uuid,pci.bus_id,name,driver_version,temperature.gpu,clocks.current.graphics,power.draw,clocks_event_reasons.active'

def now():return datetime.now(timezone.utc).isoformat()

def run_record(argv,directory,label,timeout=10):
    """Every attempt keeps immutable literal argv, output and exit, including timeouts."""
    directory=Path(directory);directory.mkdir(parents=True,exist_ok=True)
    started=now();tick=time.perf_counter();out=b'';err=b'';code=None;failure=None
    try:
        result=subprocess.run(argv,capture_output=True,timeout=timeout,shell=False,
                              creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
        out,err,code=result.stdout,result.stderr,result.returncode
    except subprocess.TimeoutExpired as error:
        out,err=error.stdout or b'',error.stderr or b'';failure='timeout'
    except OSError as error:failure=str(error)
    outputs={}
    for suffix,data in (('stdout',out),('stderr',err)):
        path=directory/(label+'.'+suffix)
        with path.open('xb') as stream:stream.write(data)
        outputs[suffix]=dict(path=path.name,sha256=sha256_bytes(data))
    record=dict(argv=list(argv),startedAtUtc=started,completedAtUtc=now(),durationSeconds=time.perf_counter()-tick,
                exitCode=code,failure=failure,outputs=outputs)
    write_new(directory/(label+'.command.json'),record)
    require(code==0 and failure is None,'Diagnostic command failed; immutable attempt retained: '+label)
    return out,record

def parse_inventory(raw):
    try:rows=list(csv.reader(io.StringIO(raw.decode('utf-8-sig'))))
    except (UnicodeError,csv.Error) as error:raise ContractError('Malformed provider CSV') from error
    require(rows,'Provider returned no devices');result=[];uuids=set();pcis=set()
    for row in rows:
        row=[v.strip() for v in row];require(len(row)==8,'Unexpected provider column count')
        uuid,pci,name,driver,temp,clock,power,mask=row
        import re
        require(re.fullmatch(r'GPU-[0-9a-fA-F-]+',uuid) and re.fullmatch(r'[0-9a-fA-F]{8}:[0-9a-fA-F]{2}:[0-9a-fA-F]{2}\.[0-7]',pci),'Physical provider identity unavailable')
        require(uuid not in uuids and pci.lower() not in pcis,'Duplicate UUID or PCI identity')
        require(name and driver and driver!='N/A','Provider name/driver unavailable')
        uuids.add(uuid);pcis.add(pci.lower())
        result.append(dict(uuid=uuid,pciBusId=pci,name=name,driverVersion=driver,temperatureC=numeric(temp,'temperature'),
                           clockMHz=numeric(clock,'clock'),powerWatts=numeric(power,'power'),eventReasons=event_reasons(mask)))
    return result

def power_status():
    require(os.name=='nt','Actual GetSystemPowerStatus requires Windows')
    class Status(ctypes.Structure):
        _fields_=[('ACLineStatus',ctypes.c_ubyte),('BatteryFlag',ctypes.c_ubyte),('BatteryLifePercent',ctypes.c_ubyte),
                  ('SystemStatusFlag',ctypes.c_ubyte),('BatteryLifeTime',ctypes.c_uint32),('BatteryFullLifeTime',ctypes.c_uint32)]
    value=Status();require(ctypes.windll.kernel32.GetSystemPowerStatus(ctypes.byref(value))!=0,'GetSystemPowerStatus failed')
    return decode_power({name:getattr(value,name) for name,_ in Status._fields_})

def decode_power(raw):
    status=raw['ACLineStatus'];return dict(raw=raw,powerSource={0:'battery',1:'ac',255:'unknown'}.get(status,'unknown'))

def classify_window_facts(pid,hwnd,candidates,owner_pid,foreground,foreground_pid,visible,iconic):
    reasons=[]
    if hwnd is None:
        if len(candidates)!=1:reasons.append("missing-or-ambiguous-client-window")
        else:hwnd=candidates[0]
    if owner_pid!=pid:reasons.append("window-owner-pid-mismatch")
    if not visible:reasons.append("window-hidden")
    if iconic:reasons.append("window-minimized")
    if foreground!=hwnd or foreground_pid!=pid:reasons.append("client-not-foreground")
    return dict(status="failed" if reasons else "controlled",observedAtUtc=now(),requestedPid=pid,hwnd=hwnd,
                candidates=candidates,ownerPid=owner_pid,foregroundHwnd=foreground,foregroundOwnerPid=foreground_pid,
                visible=visible,iconic=iconic,reasons=reasons,semantics="WS_VISIBLE is not physical unobscured coverage")

def window_facts(pid=None,hwnd=None):
    if pid is None:return dict(status="unavailable",observedAtUtc=now(),reason="No client PID supplied; foreground continuity unobserved")
    require(type(pid) is int and pid>0,"Client PID must be a positive integer")
    require(hwnd is None or type(hwnd) is int and hwnd>0,"Client HWND must be a positive integer")
    require(os.name=="nt","Actual client window facts require Windows")
    from ctypes import wintypes
    user=ctypes.WinDLL("user32",use_last_error=True)
    user.GetForegroundWindow.restype=wintypes.HWND
    user.GetWindowThreadProcessId.argtypes=[wintypes.HWND,ctypes.POINTER(wintypes.DWORD)]
    for name in ("IsWindowVisible","IsIconic"):
        getattr(user,name).argtypes=[wintypes.HWND];getattr(user,name).restype=wintypes.BOOL
    def owner(handle):
        value=wintypes.DWORD();user.GetWindowThreadProcessId(handle,ctypes.byref(value));return value.value
    candidates=[]
    callback_type=ctypes.WINFUNCTYPE(wintypes.BOOL,wintypes.HWND,wintypes.LPARAM)
    @callback_type
    def visit(handle,_):
        if owner(handle)==pid:candidates.append(int(handle))
        return True
    user.EnumWindows.argtypes=[callback_type,wintypes.LPARAM];user.EnumWindows.restype=wintypes.BOOL
    require(user.EnumWindows(visit,0)!=0,"EnumWindows failed")
    selected=hwnd if hwnd is not None else candidates[0] if len(candidates)==1 else None
    foreground=user.GetForegroundWindow();foreground=int(foreground) if foreground else None
    return classify_window_facts(pid,selected,candidates,owner(selected) if selected else None,foreground,
                                 owner(foreground) if foreground else None,bool(selected and user.IsWindowVisible(selected)),
                                 bool(selected and user.IsIconic(selected)))

def resolve_executable(path):
    value=Path(path)
    require(value.is_absolute(),"Provider executable path must be absolute")
    value=value.resolve(strict=True)
    require(value.is_file() and value.suffix.lower()==".exe","Provider must name an existing explicit .exe file")
    return str(value)

def executable_hash(path):
    return sha256_bytes(Path(path).read_bytes())

def acquire(output,executable,native=None,expected_uuid=None,samples=3,interval=1.0,client_pid=None,client_hwnd=None):
    require(type(samples) is int and 1<=samples<=43201,'Sample count outside bounded diagnostic range')
    require(0.1<=interval<=5,'Diagnostic interval must be 0.1..5 seconds')
    output=Path(output);output.mkdir(parents=True,exist_ok=False)
    series=DiagnosticSeries(samples);started=now();status='failed';failure=None
    before_hash=None;after_hash=None;version=None
    try:
        executable=resolve_executable(executable)
        before_hash=executable_hash(executable)
        version_raw,version_command=run_record([str(executable),"--version"],output,"provider-version")
        version_text=version_raw.decode("utf-8-sig").strip()
        require(version_text,"Provider version output is unavailable")
        version=dict(rawText=version_text,rawSha256=sha256_bytes(version_raw),commandRecord="provider-version.command.json")
        for i in range(samples):
            raw,command=run_record([str(executable),'--query-gpu='+FIELDS,'--format=csv,noheader,nounits'],output,f'sensor-{i:05d}')
            inventory=parse_inventory(raw);power=power_status();window=window_facts(client_pid,client_hwnd);at=now()
            mapping=candidate_mapping(native,inventory,expected_uuid)
            gap_valid=not series.samples or (utc(at)-utc(series.samples[-1]['observedAtUtc'])).total_seconds()<=5
            series.add(dict(observedAtUtc=at,acquisitionStartedAtUtc=command['startedAtUtc'],devices=inventory,power=power,window=window,
                            rawSha256=sha256_bytes(raw),commandRecord=f'sensor-{i:05d}.command.json'))
            atomic_diagnostic_snapshot(output/'latest-unbound.json',series.snapshot(inventory,mapping))
            require(gap_valid,'Sensor/window acquisition gap exceeds five seconds; facts retained')
            require(window['status']!='failed','Client foreground control failed; actual facts retained')
            if i+1<samples:time.sleep(interval)
        status='diagnostic-complete'
    except Exception as error:failure=str(error)
    finally:
        try:
            after_hash=executable_hash(executable) if before_hash is not None else None
            if before_hash is not None and after_hash!=before_hash:
                status='failed';failure=(failure+'; ' if failure else '')+'Provider executable changed during acquisition'
        except OSError as error:
            status='failed';failure=(failure+'; ' if failure else '')+'Provider final executable hash unavailable: '+str(error)
        record=dict(kind='unbound-provider-acquisition',startedAtUtc=started,completedAtUtc=now(),status=status,failure=failure,
                    samplesRetained=len(series.samples),providerVersion=version,executableSha256Before=before_hash,executableSha256After=after_hash,
                    querySchema=FIELDS,physicalAcceptance='open',wave2Ready=False,
                    qualifiedPublication='blocked: renderer physical identity attestation unavailable')
        write_new(output/'attempt.json',record)
    require(status=='diagnostic-complete',failure or 'Acquisition failed')
    return record
