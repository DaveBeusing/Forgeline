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

def acquire(output,executable,native=None,expected_uuid=None,samples=3,interval=1.0):
    require(type(samples) is int and 1<=samples<=43201,'Sample count outside bounded diagnostic range')
    require(0.1<=interval<=5,'Diagnostic interval must be 0.1..5 seconds')
    output=Path(output);output.mkdir(parents=True,exist_ok=False)
    series=DiagnosticSeries(samples);records=[];started=now();status='failed';failure=None
    try:
        for i in range(samples):
            raw,command=run_record([str(executable),'--query-gpu='+FIELDS,'--format=csv,noheader,nounits'],output,f'sensor-{i:05d}')
            inventory=parse_inventory(raw);power=power_status();at=now()
            mapping=candidate_mapping(native,inventory,expected_uuid)
            if series.samples:
                require((utc(at)-utc(series.samples[-1]['observedAtUtc'])).total_seconds()<=5,'Sensor acquisition gap exceeds five seconds')
            series.add(dict(observedAtUtc=at,acquisitionStartedAtUtc=command['startedAtUtc'],devices=inventory,power=power,
                            rawSha256=sha256_bytes(raw),commandRecord=f'sensor-{i:05d}.command.json'))
            atomic_diagnostic_snapshot(output/'latest-unbound.json',series.snapshot(inventory,mapping))
            if i+1<samples:time.sleep(interval)
        status='diagnostic-complete'
    except Exception as error:failure=str(error)
    finally:
        record=dict(kind='unbound-provider-acquisition',startedAtUtc=started,completedAtUtc=now(),status=status,failure=failure,
                    samplesRetained=len(series.samples),physicalAcceptance='open',wave2Ready=False,
                    qualifiedPublication='blocked: renderer physical identity attestation unavailable')
        write_new(output/'attempt.json',record)
    require(status=='diagnostic-complete',failure or 'Acquisition failed')
    return record
