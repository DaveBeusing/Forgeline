"""Bounded append-only raw sensor journal with constant-size progress snapshots."""
from __future__ import annotations
from dataclasses import dataclass
import base64
from datetime import datetime,timezone
import hashlib,json,os
from pathlib import Path
from baseline_contracts import ContractError,require,utc,write_new,parse_json_bytes

@dataclass(frozen=True)
class SensorJournalReceipt:
    journal_path: Path
    row_bytes: bytes
    prefix_receipt_bytes: bytes


class SensorJournal:
    def __init__(self,directory,capacity=43201,byte_budget=64*1024*1024):
        require(type(capacity) is int and 1<=capacity<=43201,'Sensor sample capacity outside bound')
        require(type(byte_budget) is int and 1<=byte_budget<=64*1024*1024,'Sensor journal byte budget outside bound')
        self.directory=Path(directory);self.directory.mkdir(parents=True,exist_ok=False)
        self.path=self.directory/'observations.jsonl';self.stream=self.path.open('xb')
        self.capacity=capacity;self.byte_budget=byte_budget;self.count=0;self.bytes=0;self.digest=hashlib.sha256();self.last=None;self.failed=None
    def append(self,sample,stdout=b'',stderr=b''):
        require(self.failed is None,'Failed sensor journal cannot resume/select a winning tail')
        try:
            at=utc(sample.get('observedAtUtc'))
            require(self.count<self.capacity,'Sensor sample capacity exhausted')
            gap=None if self.last is None else (at-utc(self.last['observedAtUtc'])).total_seconds()
            require(gap is None or gap>0,'Sensor observation timestamps must increase')
            row=dict(index=self.count,sample=sample,rawStdoutBase64=base64.b64encode(stdout).decode('ascii'),rawStderrBase64=base64.b64encode(stderr).decode('ascii'))
            data=(json.dumps(row,separators=(',',':'),allow_nan=False)+'\n').encode('utf-8')
            require(self.bytes+len(data)<=self.byte_budget,'Sensor raw byte budget exhausted')
            self.stream.write(data);self.stream.flush();os.fsync(self.stream.fileno())
            self.digest.update(data);self.bytes+=len(data);self.count+=1;self.last=sample
            receipt=self._snapshot()
            require(gap is None or gap<=5,'Sensor/control gap exceeds five seconds; raw failed observation retained')
            return SensorJournalReceipt(self.path,data,receipt)
        except Exception as error:
            self.failed=str(error);self._snapshot();raise
    def _snapshot(self):
        value=dict(schemaVersion=1,kind='physical-sensor-journal-progress',qualified=False,rows=self.count,journal='observations.jsonl',
                   prefixBytes=self.bytes,prefixSha256=self.digest.hexdigest(),lastObservation=self.last,failure=self.failed)
        data=json.dumps(value,separators=(',',':'),allow_nan=False).encode('utf-8')
        temporary=self.directory/'latest.pending'
        with temporary.open('xb') as stream:
            stream.write(data);stream.flush();os.fsync(stream.fileno())
        temporary.replace(self.directory/'latest.json')
        return data
    def close(self):
        self.stream.close()
        write_new(self.directory/'final.json',dict(kind='physical-sensor-journal-final',rows=self.count,bytes=self.bytes,sha256=self.digest.hexdigest(),failure=self.failed,qualified=False))
    def __enter__(self):return self
    def __exit__(self,*_):self.close()

def read_journal(directory):
    directory=Path(directory);final=parse_json_bytes((directory/'final.json').read_bytes())
    require(final.get('failure') is None,'Failed journal cannot qualify a cohort')
    require((directory/'observations.jsonl').stat().st_size<=64*1024*1024,'Sensor journal exceeds bounded input size')
    raw=(directory/'observations.jsonl').read_bytes()
    require(len(raw)==final['bytes'] and hashlib.sha256(raw).hexdigest()==final['sha256'],'Sensor journal was truncated or changed')
    rows=[parse_json_bytes(line) for line in raw.splitlines()]
    require(len(rows)==final['rows'] and [r['index'] for r in rows]==list(range(len(rows))),'Sensor journal chronology was changed')
    previous=None
    from provider import parse_inventory
    for row in rows:
        require(type(row['index']) is int,'Sensor row index must be an integer')
        sample=row['sample'];at=utc(sample['observedAtUtc'])
        require(previous is None or 0<(at-previous).total_seconds()<=5,'Sensor chronology/gap invalid')
        require(parse_inventory(base64.b64decode(row['rawStdoutBase64'],validate=True))==sample['devices'],'Parsed sensors differ from exact raw provider output')
        previous=at
    return rows
