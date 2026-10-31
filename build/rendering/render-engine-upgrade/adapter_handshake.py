"""Live exact-adapter handshake; all query evidence remains outside frame timing."""
import json
from pathlib import Path
from datetime import datetime,timezone
from adapter_identity import bind_adapter,observe_process,query_cuda_identity_bounded
from baseline_contracts import parse_json_bytes,require,sha256_bytes,write_new
from provider import FIELDS,parse_inventory,resolve_executable,run_record

def complete_checkpoint(directory,command_context,dll_path,sensor_executable,before_publish=None):
    directory=Path(directory);checkpoint_bytes=(directory/'checkpoint.json').read_bytes()
    checkpoint=parse_json_bytes(checkpoint_bytes);pid=command_context['processId']
    require(checkpoint.get('adapterIdentity',{}).get('processId')==pid,'Producer checkpoint is not the actual child process')
    before=observe_process(pid);cuda=query_cuda_identity_bounded(dll_path,directory)
    write_new(directory/'cuda-inventory.json',cuda)
    executable=resolve_executable(sensor_executable);binary_before=sha256_bytes(Path(executable).read_bytes())
    version_raw,_=run_record([executable,'--version'],directory,'binding-provider-version')
    require(version_raw.strip(),'Provider version is unavailable')
    raw,_=run_record([executable,'--query-gpu='+FIELDS,'--format=csv,noheader,nounits'],directory,'binding-sensor-inventory')
    inventory=parse_inventory(raw);after=observe_process(pid)
    binary_after=sha256_bytes(Path(executable).read_bytes());require(binary_before==binary_after,'Sensor executable changed during exact binding')
    proof=bind_adapter(checkpoint_bytes,cuda,inventory,before,after,command_context)
    require(proof['exactPhysicalMappingAvailable'] is True,'Only a live actual launch may complete a physical identity handshake')
    proof.update(sensorExecutableSha256Before=binary_before,sensorExecutableSha256After=binary_after,providerVersionSha256=sha256_bytes(version_raw),
                 cudaInventoryFileSha256=sha256_bytes((directory/'cuda-inventory.json').read_bytes()),sensorRawSha256=sha256_bytes(raw))
    if before_publish is not None:before_publish(proof)
    temporary=directory/'binding.pending.json';write_new(temporary,proof)
    temporary.rename(directory/'binding.json')
    return proof
