"""Ordinary post-window v1: fresh query plus durable journal receipt, never a stability approval."""
import base64,time,hashlib,re,json
from datetime import datetime,timezone,timedelta
from baseline_contracts import require,parse_json_bytes,sha256_bytes,utc
from diagnostic_control import atomic_new

def json_exact(left,right):
    return json.dumps(left,sort_keys=True,separators=(',',':'),allow_nan=False)==json.dumps(right,sort_keys=True,separators=(',',':'),allow_nan=False)

IDENTITY=('sourceSha','runtimeFingerprint','runId','checkpointSha256','processId','nativeWindowHandle')
def integer(value,name,minimum=0):
    require(type(value) is int and value>=minimum,'Malformed post-window '+name);return value

def validate_ready(raw,checkpoint_bytes):
    value=parse_json_bytes(raw);checkpoint=parse_json_bytes(checkpoint_bytes)
    require(type(value.get('schemaVersion')) is int and value['schemaVersion']==1 and value.get('kind')=='ordinary-post-window-control-ready' and value.get('qualified') is False,'Wrong ordinary post-window ready contract')
    expected=dict(sourceSha=checkpoint['sourceSha'],runtimeFingerprint=checkpoint['runtimeFingerprint'],runId=checkpoint['runId'],checkpointSha256=sha256_bytes(checkpoint_bytes),processId=checkpoint['adapterIdentity']['processId'],nativeWindowHandle=checkpoint['nativeWindowHandle'])
    require(all(type(value.get(k)) is type(v) and (value[k].lower()==v.lower() if k in ('runtimeFingerprint','checkpointSha256') else value[k]==v) for k,v in expected.items()),'Post-window ready belongs to another source/runtime/run/checkpoint/PID/HWND')
    integer(value.get('sampleCount'),'sample count',1);last=integer(value.get('lastMeasuredEndTick'),'last measured endpoint',1);sealed=integer(value.get('sealedEndTick'),'sealed endpoint',last)
    cycle=value.get('lastCycleEndTick');require(cycle is None or type(cycle) is int and last<=cycle<=sealed,'Invalid last owner cycle endpoint')
    require(value.get('endpointKind')==('owner-cycle-v1' if cycle is not None else 'native-legacy-frame') and value.get('ownerCycleAvailable') is (cycle is not None),'Owner-cycle absence cannot be relabelled as available')
    case=value.get('caseIdentity');require(type(case) is dict and type(case.get('caseKey')) is str and case['caseKey'] and case.get('kind') in ('native-reference','authored-world-fixture','actual-live-session'),'Actual case identity unavailable')
    require(type(case.get('recipeAvailable')) is bool,'Recipe availability missing')
    recipe=case.get('recipeFingerprint');require((type(recipe) is str and re.fullmatch('[0-9a-fA-F]{64}',recipe)) if case['recipeAvailable'] else recipe is None,'Unavailable recipe cannot be copied from an authored label')
    require(type(case.get('profileFingerprint')) is str and len(case['profileFingerprint'])==64,'Actual selected profile proof unavailable')
    if case['kind']=='actual-live-session':
        require(case['recipeAvailable'] is False and case.get('recipeFingerprint') is None,'Actual live session cannot borrow an authored recipe')
        integer(case.get('simulationSession'),'actual simulation session',1)
        require(type(case.get('sessionOriginFingerprint')) is str and re.fullmatch('[0-9a-fA-F]{64}',case['sessionOriginFingerprint']),'Actual session origin proof missing')
    submission=value.get('lastSubmission');require(type(submission) is dict and set(submission)=={'submission','submittedFence','generation','resourceGeneration'},'Actual final submission identity missing')
    for key in submission:integer(submission[key],'final '+key,1)
    frequency=integer(value.get('frequencyHz'),'frequency',1);require(sealed-(cycle if cycle is not None else last)<=5*frequency,'Ready publication exceeds existing five-second endpoint coverage')
    profile_raw=base64.b64decode(case.get('profileInputBytesBase64',''),validate=True)
    require(sha256_bytes(profile_raw)==case['profileFingerprint'].lower(),'Actual profile input bytes/hash differ')
    require(type(parse_json_bytes(profile_raw)) is dict,'Actual profile bytes are not a composition object')
    utc(value['sealedAtUtc']);return value

def _checked_ack(ready_bytes,checkpoint_bytes,receipt,*,command_deadline,still_allowed,binding,evaluated_at):
    require(time.monotonic()<command_deadline and still_allowed(),'Original command deadline/process no longer permits a post-window ACK')
    ready=validate_ready(ready_bytes,checkpoint_bytes);ready_sha=sha256_bytes(ready_bytes)
    from sensor_journal import SensorJournalReceipt
    require(type(receipt) is SensorJournalReceipt,'Only the source-owned post-fsync journal receipt may request an ACK')
    row_raw=receipt.row_bytes;row=parse_json_bytes(row_raw);prefix_raw=receipt.prefix_receipt_bytes;prefix=parse_json_bytes(prefix_raw);sample=row['sample']
    require(set(prefix)=={'schemaVersion','kind','qualified','rows','journal','prefixBytes','prefixSha256','lastObservation','failure'} and type(prefix.get('schemaVersion')) is int and prefix['schemaVersion']==1 and prefix.get('kind')=='physical-sensor-journal-progress' and prefix.get('qualified') is False and prefix.get('journal')=='observations.jsonl','Malformed durable journal receipt schema')
    require(type(prefix.get('rows')) is int and prefix['rows']>0 and type(prefix.get('prefixBytes')) is int and len(row_raw)<=prefix['prefixBytes']<=64*1024*1024 and type(prefix.get('prefixSha256')) is str and re.fullmatch('[0-9a-f]{64}',prefix['prefixSha256']),'Malformed durable journal receipt counts/hash')
    require(receipt.journal_path.is_file() and not receipt.journal_path.is_symlink(),'Actual durable journal is unavailable')
    with receipt.journal_path.open('rb') as stream:
        digest=hashlib.sha256();remaining=prefix['prefixBytes']
        while remaining:
            block=stream.read(min(65536,remaining));require(block,'Actual durable journal prefix was truncated');digest.update(block);remaining-=len(block)
        require(digest.hexdigest()==prefix['prefixSha256'],'Actual durable journal prefix hash differs')
        stream.seek(prefix['prefixBytes']-len(row_raw));require(stream.read(len(row_raw))==row_raw,'Receipt row is not the last durable prefix row')
    require(type(row.get('index')) is int and row['index']>=0 and prefix['rows']==row['index']+1 and prefix.get('failure') is None,'Durable receipt does not bind this last row')
    require(json_exact(prefix['lastObservation'],sample) and type(prefix.get('prefixBytes')) is int and prefix['prefixBytes']>=len(row_raw) and type(prefix.get('prefixSha256')) is str and len(prefix['prefixSha256'])==64,'Durable prefix/sample receipt differs')
    require(sample.get('postWindowReadySha256')==ready_sha,'Query was already in flight before this ready request')
    require(utc(ready['sealedAtUtc'])<=utc(sample['acquisitionStartedAtUtc'])<=utc(sample['observedAtUtc']),'Post-window query did not start after the sealed ready')
    require(sample.get('failure') is None and type(sample.get('command',{}).get('exitCode')) is int and sample['command']['exitCode']==0 and sample['command'].get('timedOut') is False,'Post-window query failed')
    from provider import parse_inventory
    from adapter_identity import normalized_pci
    require(json_exact(parse_inventory(base64.b64decode(row['rawStdoutBase64'],validate=True)),sample['devices']),'Post-window sensor values differ from original raw query bytes')
    require(binding.get('exactPhysicalMappingAvailable') is True and binding['nativeManifestSha256'].lower()==sha256_bytes(checkpoint_bytes),'Post-window provider binding belongs to another checkpoint')
    actual=[d for d in sample['devices'] if d['uuid']==binding['uuid'] and normalized_pci(d['pciBusId'])==normalized_pci(binding['pciBusId'])]
    require(len(actual)==1,'Actual bound provider GPU is missing or ambiguous in fresh query')
    observed=utc(sample['observedAtUtc']);current=evaluated_at
    endpoint=utc(ready['sealedAtUtc'])-timedelta(seconds=(ready['sealedEndTick']-(ready['lastCycleEndTick'] if ready['lastCycleEndTick'] is not None else ready['lastMeasuredEndTick']))/ready['frequencyHz'])
    require(0<=(observed-endpoint).total_seconds()<=5 and 0<=(current-endpoint).total_seconds()<=5 and current>=observed,'Post-window row/ACK exceeds existing five-second coverage/freshness')
    from baseline_admission import actual_foreground
    window=sample['window'];actual_foreground(window,ready['processId'],ready['nativeWindowHandle'])
    require(type(window.get('foregroundHwnd')) is int and type(window.get('foregroundOwnerPid')) is int,'Actual foreground identity types differ')
    require(time.monotonic()<command_deadline and still_allowed(),'Original command bound/process expired before post-window ACK')
    return dict(schemaVersion=1,kind='ordinary-post-window-control-ack',qualified=False,readySha256=ready_sha,
        **{key:ready[key] for key in IDENTITY},providerUuid=binding['uuid'],providerPciBusId=binding['pciBusId'],caseIdentity=ready['caseIdentity'],lastSubmission=ready['lastSubmission'],sampleIndex=row['index'],
        rowBytesBase64=base64.b64encode(row_raw).decode(),rowSha256=sha256_bytes(row_raw),
        prefixReceiptBytesBase64=base64.b64encode(prefix_raw).decode(),prefixReceiptSha256=sha256_bytes(prefix_raw),
        acknowledgedAtUtc=evaluated_at.isoformat(),scope='Fresh actual query start after seal; durable row/prefix receipt; no hardware policy or continuous-visibility approval')

def make_ack(ready_bytes,checkpoint_bytes,receipt,*,command_deadline,still_allowed,binding):
    return _checked_ack(ready_bytes,checkpoint_bytes,receipt,command_deadline=command_deadline,
        still_allowed=still_allowed,binding=binding,evaluated_at=datetime.now(timezone.utc))

def validate_retained(directory,checkpoint_bytes,binding,*,last_submission,last_measured_end,last_cycle_end,case_identity,sample_count,command_completed_at):
    """Offline check after original command/source/process binding gates; no new live ACK."""
    from sensor_journal import SensorJournalReceipt
    ready_raw=(directory/'ready.json').read_bytes();ready=validate_ready(ready_raw,checkpoint_bytes)
    ack_raw=(directory/'ack.json').read_bytes();ack=parse_json_bytes(ack_raw)
    require(type(last_measured_end) is int and (last_cycle_end is None or type(last_cycle_end) is int),'Final source endpoints must be strict integers')
    require(json_exact(ready['lastSubmission'],last_submission) and ready['lastMeasuredEndTick']==last_measured_end and ready['lastCycleEndTick']==last_cycle_end and
        json_exact(ready['caseIdentity'],case_identity) and type(sample_count) is int and ready['sampleCount']==sample_count,
        'Ordinary post-window ready differs from actual final frame/cycle/case/sample identity')
    at=utc(ack['acknowledgedAtUtc']);context=binding['commandContext']
    require(type(context.get('timeoutSeconds')) is int and 1<=context['timeoutSeconds']<=600 and
        utc(context['startedAtUtc'])<=at<=min(utc(context['startedAtUtc'])+timedelta(seconds=context['timeoutSeconds']),utc(command_completed_at)),
        'Retained ACK lies outside original actual command lifetime/budget')
    receipt=SensorJournalReceipt(directory.parent/'sensors'/'observations.jsonl',base64.b64decode(ack['rowBytesBase64'],validate=True),base64.b64decode(ack['prefixReceiptBytesBase64'],validate=True))
    expected=_checked_ack(ready_raw,checkpoint_bytes,receipt,command_deadline=float('inf'),still_allowed=lambda:True,binding=binding,evaluated_at=at)
    require(json_exact(ack,expected),'Retained ACK bytes/identities/row/prefix differ from actual source-derived receipt')
    return dict(schemaVersion=1,kind='verified-ordinary-post-window-control',readySha256=sha256_bytes(ready_raw),ackSha256=sha256_bytes(ack_raw),lastSubmission=last_submission,qualified=False)

def publish_ack(directory,ready_bytes,checkpoint_bytes,receipt,*,command_deadline,still_allowed,binding):
    value=make_ack(ready_bytes,checkpoint_bytes,receipt,command_deadline=command_deadline,still_allowed=still_allowed,binding=binding)
    require(time.monotonic()<command_deadline and still_allowed(),'Original command bound/process expired before ACK publication')
    atomic_new(directory/'ack.json',value);return value


class PostWindowSupervisor:
    """Main loop arms a ready; observer snapshots it BEFORE starting the next query."""
    def __init__(self,directory,checkpoint_bytes,binding,command_deadline,still_allowed,wake):
        import threading
        self.directory=directory;self.checkpoint_bytes=checkpoint_bytes;self.binding=binding
        self.deadline=command_deadline;self.still_allowed=still_allowed;self.wake=wake
        self._gate=threading.Lock();self._ready=None;self._ack=None
    @property
    def completed(self):
        with self._gate:return self._ack is not None
    def poll(self):
        path=self.directory/'ready.json'
        if not path.exists():return
        require(time.monotonic()<self.deadline and self.still_allowed(),'Original command bound/process expired before post-window ready')
        require(not path.is_symlink() and path.stat().st_size<=65536,'Post-window ready path/size invalid')
        raw=path.read_bytes();validate_ready(raw,self.checkpoint_bytes)
        with self._gate:
            require(self._ready is None or self._ready==raw,'Sealed post-window ready bytes changed')
            if self._ready is None:self._ready=raw;self.wake.set()
    def begin_query(self):
        with self._gate:return self._ready if self._ack is None else None
    def finish_query(self,ready_bytes,receipt):
        if ready_bytes is None:return
        with self._gate:
            require(ready_bytes==self._ready and self._ack is None,'Post-window fresh-query request changed or was reused')
            self._ack=publish_ack(self.directory,ready_bytes,self.checkpoint_bytes,receipt,
                command_deadline=self.deadline,still_allowed=self.still_allowed,binding=self.binding)
    def evidence(self):
        with self._gate:return dict(schemaVersion=1,kind='ordinary-post-window-supervisor',completed=self._ack is not None,
            readySha256=sha256_bytes(self._ready) if self._ready else None,ackSha256=sha256_bytes((self.directory/'ack.json').read_bytes()) if self._ack else None,
            qualified=False)
