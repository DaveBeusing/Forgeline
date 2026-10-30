"""Query-only installed CUDA identity bridge; exact matches never use adapter names."""
from __future__ import annotations
import ctypes
from datetime import datetime,timezone
import hashlib
from pathlib import Path
import re
import uuid
from baseline_contracts import ContractError,canonical,parse_json_bytes,require,sha256_bytes,utc

def luid_hex(raw):
    require(type(raw) is bytes and len(raw)==8,"CUDA LUID requires exactly eight raw bytes")
    return f"{int.from_bytes(raw,'little'):016x}"

def query_cuda_identity(dll_path):
    path=Path(dll_path)
    require(path.is_absolute() and path.name.lower()=='nvcuda.dll','An absolute installed nvcuda.dll path is required')
    path=path.resolve(strict=True);before=sha256_bytes(path.read_bytes())
    record=dict(kind='cuda-physical-device-inventory',schemaVersion=1,dllPath=str(path),dllSha256Before=before,
                startedAtUtc=datetime.now(timezone.utc).isoformat(),devices=[],calls=[])
    try:
        driver=ctypes.WinDLL(str(path))
        def call(name,signature,*args):
            function=getattr(driver,name);function.argtypes=signature;function.restype=ctypes.c_int
            code=function(*args);record['calls'].append(dict(function=name,result=code))
            require(code==0,name+' failed: '+str(code))
        call('cuInit',[ctypes.c_uint],0)
        version=ctypes.c_int();call('cuDriverGetVersion',[ctypes.POINTER(ctypes.c_int)],ctypes.byref(version));record['driverApiVersion']=version.value
        count=ctypes.c_int();call('cuDeviceGetCount',[ctypes.POINTER(ctypes.c_int)],ctypes.byref(count))
        require(0<count.value<=128,'CUDA device count unavailable or outside bound')
        for index in range(count.value):
            device=ctypes.c_int();call('cuDeviceGet',[ctypes.POINTER(ctypes.c_int),ctypes.c_int],ctypes.byref(device),index)
            luid=ctypes.create_string_buffer(8);mask=ctypes.c_uint()
            call('cuDeviceGetLuid',[ctypes.POINTER(ctypes.c_char),ctypes.POINTER(ctypes.c_uint),ctypes.c_int],luid,ctypes.byref(mask),device.value)
            identity=(ctypes.c_ubyte*16)();call('cuDeviceGetUuid_v2',[ctypes.c_void_p,ctypes.c_int],ctypes.byref(identity),device.value)
            pci=ctypes.create_string_buffer(64);call('cuDeviceGetPCIBusId',[ctypes.POINTER(ctypes.c_char),ctypes.c_int,ctypes.c_int],pci,64,device.value)
            record['devices'].append(dict(ordinal=index,luidBytesHex=luid.raw.hex(),adapterLuidHex=luid_hex(luid.raw),
                                          nodeMask=mask.value,uuid='GPU-'+str(uuid.UUID(bytes=bytes(identity))),pciBusId=pci.value.decode('ascii')))
        record['status']='available'
    except Exception as error:record.update(status='unavailable',error=str(error))
    finally:
        try:record['dllSha256After']=sha256_bytes(path.read_bytes())
        except OSError as error:record.update(status='unavailable',error=str(error))
        if record.get('dllSha256After')!=before:record.update(status='unavailable',error='CUDA driver DLL changed during identity query')
        record['completedAtUtc']=datetime.now(timezone.utc).isoformat()
    return record

def normalized_pci(raw):
    require(type(raw) is str,'PCI address missing')
    match=re.fullmatch(r'([0-9a-fA-F]{4,8}):([0-9a-fA-F]{2}):([0-9a-fA-F]{2})\.([0-7])',raw)
    require(match is not None,'Malformed full PCI address')
    domain,bus,device,function=(int(v,16) for v in match.groups())
    require(device<32,'Invalid PCI device index')
    return f'{domain:08x}:{bus:02x}:{device:02x}.{function:x}'

def observe_process(pid):
    """Actual Win32 liveness/creation identity; no process state changes."""
    require(type(pid) is int and pid>0,'A real producer PID is required')
    from ctypes import wintypes
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    kernel.OpenProcess.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD];kernel.OpenProcess.restype=wintypes.HANDLE
    kernel.CloseHandle.argtypes=[wintypes.HANDLE]
    kernel.WaitForSingleObject.argtypes=[wintypes.HANDLE,wintypes.DWORD];kernel.WaitForSingleObject.restype=wintypes.DWORD
    kernel.GetProcessTimes.argtypes=[wintypes.HANDLE]+[ctypes.POINTER(wintypes.FILETIME)]*4;kernel.GetProcessTimes.restype=wintypes.BOOL
    kernel.QueryFullProcessImageNameW.argtypes=[wintypes.HANDLE,wintypes.DWORD,wintypes.LPWSTR,ctypes.POINTER(wintypes.DWORD)];kernel.QueryFullProcessImageNameW.restype=wintypes.BOOL
    handle=kernel.OpenProcess(0x1000|0x100000,False,pid)
    require(bool(handle),'Cannot open actual producer process for identity observation')
    try:
        values=[wintypes.FILETIME() for _ in range(4)]
        require(kernel.GetProcessTimes(handle,*[ctypes.byref(value) for value in values])!=0,'GetProcessTimes failed')
        text=ctypes.create_unicode_buffer(32768);length=wintypes.DWORD(len(text))
        require(kernel.QueryFullProcessImageNameW(handle,0,text,ctypes.byref(length))!=0,'Process image identity unavailable')
        image=Path(text.value);creation=(values[0].dwHighDateTime<<32)|values[0].dwLowDateTime
        return dict(processId=pid,creationFileTimeUtc=creation,alive=kernel.WaitForSingleObject(handle,0)==258,
                    imagePath=str(image),imageSha256=sha256_bytes(image.read_bytes()),observedAtUtc=datetime.now(timezone.utc).isoformat())
    finally:kernel.CloseHandle(handle)

def bind_adapter(native_bytes,cuda_inventory,sensor_inventory,process_before=None,process_after=None,command_context=None):
    """Bind exact producer LUID/node to queried UUID+PCI, with immutable manifest bytes."""
    native=parse_json_bytes(native_bytes);identity=native.get('adapterIdentity')
    require(type(identity) is dict and type(identity.get('schemaVersion')) is int and identity['schemaVersion']==1,'Actual renderer adapter identity missing')
    luid=identity.get('adapterLuidHex');nodes=identity.get('nodeCount');raw=identity.get('queueNodeMask');mask=identity.get('effectiveNodeMask')
    require(type(luid) is str and re.fullmatch('[0-9a-f]{16}',luid) and int(luid,16)!=0,'Malformed renderer LUID')
    require(type(nodes) is int and nodes==1 and type(raw) is int and raw in (0,1),'Only documented single-node queue mapping is admitted; multi-node remains unavailable')
    require(type(mask) is int and mask==(1 if raw==0 else raw) and mask>0,'Invalid effective queue-node mask')
    require(type(identity.get('generation')) is int and identity['generation']>0 and type(identity.get('processId')) is int and identity['processId']>0,'Actual renderer process/generation missing')
    require(type(identity.get('sessionId')) is str and re.fullmatch('[0-9a-f]{32}',identity['sessionId']),'Producer-generated device session ID missing')
    require(type(cuda_inventory.get('schemaVersion')) is int and cuda_inventory['schemaVersion']==1 and cuda_inventory.get('kind')=='cuda-physical-device-inventory','Invalid CUDA inventory schema')
    require(cuda_inventory.get('status')=='available' and cuda_inventory.get('dllSha256Before')==cuda_inventory.get('dllSha256After'),'CUDA identity unavailable or changed')
    require(re.fullmatch('[0-9a-f]{64}',cuda_inventory.get('dllSha256Before','')),'CUDA DLL byte identity missing')
    devices=cuda_inventory.get('devices');require(type(devices) is list and 1<=len(devices)<=128,'Invalid bounded CUDA inventory')
    expected_calls=['cuInit','cuDriverGetVersion','cuDeviceGetCount']+['cuDeviceGet','cuDeviceGetLuid','cuDeviceGetUuid_v2','cuDeviceGetPCIBusId']*len(devices)
    calls=cuda_inventory.get('calls');require(type(calls) is list and len(calls)==len(expected_calls),'CUDA query call provenance missing')
    require(all(type(row) is dict and row.get('function')==name and type(row.get('result')) is int and row['result']==0 for row,name in zip(calls,expected_calls)),'CUDA query call provenance failed/mismatched')
    candidates=[row for row in devices if row.get('adapterLuidHex')==luid and type(row.get('nodeMask')) is int and row.get('nodeMask')==mask]
    require(len(candidates)==1,'Renderer LUID/node has no unique exact CUDA identity')
    chosen=candidates[0]
    raw_luid=chosen.get('luidBytesHex');require(type(raw_luid) is str and re.fullmatch('[0-9a-f]{16}',raw_luid),'Raw CUDA LUID bytes missing')
    require(luid_hex(bytes.fromhex(raw_luid))==luid,'CUDA raw LUID byte order/value mismatch')
    pci=normalized_pci(chosen.get('pciBusId'))
    matches=[row for row in sensor_inventory if row.get('uuid')==chosen.get('uuid') and normalized_pci(row.get('pciBusId'))==pci]
    require(len(matches)==1,'CUDA UUID/PCI has no unique exact sensor match')
    require(sum(row.get('uuid')==chosen.get('uuid') for row in sensor_inventory)==1,'Duplicated sensor UUID')
    require(sum(normalized_pci(row.get('pciBusId'))==pci for row in sensor_inventory)==1,'Duplicated sensor PCI address')
    live_bound=False
    if process_before is not None or process_after is not None:
        require(type(process_before) is dict and type(process_after) is dict,'Both actual process observations are required')
        for observation in (process_before,process_after):
            require(observation.get('alive') is True and type(observation.get('processId')) is int and observation['processId']==identity['processId'],'Actual renderer process is not alive/matched')
            require(type(identity.get('processStartedFileTimeUtc')) is int and identity['processStartedFileTimeUtc']>0 and type(observation.get('creationFileTimeUtc')) is int and observation['creationFileTimeUtc']==identity['processStartedFileTimeUtc'],'Renderer process creation identity mismatch; boot-local LUID is insufficient')
        require(utc(process_before['observedAtUtc'])<=utc(cuda_inventory['startedAtUtc'])<=utc(cuda_inventory['completedAtUtc'])<=utc(process_after['observedAtUtc']),'CUDA query is outside actual renderer lifetime observations')
        require(process_before.get('imageSha256')==process_after.get('imageSha256') and re.fullmatch('[0-9a-f]{64}',process_before.get('imageSha256','')),'Actual renderer process image changed/unavailable')
        require(type(command_context) is dict,'Actual launch command context is required')
        require(native.get('kind')=='actual-renderer-identity-checkpoint' and type(native.get('runId')) is str and re.fullmatch('[0-9a-f]{32}',native['runId']),'Actual producer-generated run checkpoint missing')
        require(type(command_context.get('processId')) is int and command_context['processId']==identity['processId'],'Checkpoint PID differs from actual launched child')
        for name in ('sourceSha','runtimeFingerprint','clientAssemblySha256'):
            require(type(command_context.get(name)) is str and native.get(name)==command_context[name],'Actual launched source/runtime/client assembly mismatch: '+name)
        created=utc(native['createdAtUtc']);query_start=utc(cuda_inventory['startedAtUtc'])
        require(utc(command_context['startedAtUtc'])<=created<=utc(process_before['observedAtUtc']) and 0<=(query_start-created).total_seconds()<=30,'Stale or replayed producer checkpoint')
        live_bound=True
    return dict(schemaVersion=1,kind='adapter-provider-binding',structuralIdentityMatch=True,nativeManifestSha256=sha256_bytes(native_bytes),
                rendererIdentity=identity,cudaInventorySha256=sha256_bytes(canonical(cuda_inventory)),sensorInventorySha256=sha256_bytes(canonical(sensor_inventory)),
                uuid=chosen['uuid'],pciBusId=pci,exactPhysicalMappingAvailable=live_bound,processBefore=process_before,processAfter=process_after,commandContext=command_context,
                method='actual ID3D12Device LUID/direct-queue node -> cuDeviceGetLuid/Uuid_v2/PCIBusId -> sensor UUID+PCI',
                sourceTrust='Contract source, manifest source and hardware policy still require independent admission')


def query_cuda_identity_bounded(dll_path,directory,timeout_seconds=10):
    """A stuck driver query is isolated in a killable child with retained command output."""
    require(type(timeout_seconds) is int and 1<=timeout_seconds<=10,'CUDA helper timeout must be bounded to ten seconds')
    import sys
    from provider import run_record
    from baseline_contracts import read_json,write_new
    directory=Path(directory);result_path=directory/'cuda-query-result.json'
    helper=Path(__file__).resolve();interpreter=Path(sys.executable).resolve()
    write_new(directory/'cuda-probe-source.json',dict(helperSha256=sha256_bytes(helper.read_bytes()),interpreterSha256=sha256_bytes(interpreter.read_bytes()),timeoutSeconds=timeout_seconds,pythonVersion=sys.version))
    run_record([str(interpreter),str(helper),'--query','--dll',str(dll_path),'--output',str(result_path)],directory,'cuda-probe',timeout=timeout_seconds)
    return read_json(result_path)

if __name__=='__main__':
    import argparse,sys
    from baseline_contracts import write_new
    parser=argparse.ArgumentParser(description='Query-only bounded child CUDA inventory; no context or kernel')
    parser.add_argument('--query',action='store_true',required=True);parser.add_argument('--dll',required=True);parser.add_argument('--output',required=True)
    args=parser.parse_args()
    result=query_cuda_identity(args.dll);write_new(args.output,result)
    sys.exit(0 if result['status']=='available' else 2)
