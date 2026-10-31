"""Opt-in external setup handoff; never activates a window or extends producer deadlines."""
from datetime import datetime,timezone,timedelta
import time
from baseline_contracts import parse_json_bytes,require,sha256_bytes,write_new,utc
from provider import window_facts

def atomic_new(path,value):
    require(not path.exists(),'Diagnostic handoff must be fresh; prior runs cannot be overwritten')
    pending=path.with_name(path.name+'.pending');write_new(pending,value);pending.rename(path)

def hold_for_operator(output,checkpoint_bytes,still_allowed):
    checkpoint=parse_json_bytes(checkpoint_bytes);pid=checkpoint['adapterIdentity']['processId'];hwnd=checkpoint['nativeWindowHandle']
    expires=utc(checkpoint['createdAtUtc'])+timedelta(seconds=30)
    remaining=(expires-datetime.now(timezone.utc)).total_seconds()
    deadline=time.monotonic()+max(0,min(30,remaining))
    identity=dict(checkpointSha256=sha256_bytes(checkpoint_bytes),runId=checkpoint['runId'],processId=pid,nativeWindowHandle=hwnd)
    record=dict(schemaVersion=1,kind='diagnostic-setup-handoff',qualified=False,**identity,expiresAtUtc=expires.isoformat(),
                before=window_facts(pid,hwnd),status='waiting',action='External operator may focus only this verified window before binding release; no automated action')
    try:
        require(record['before'].get('ownerPid')==pid and record['before'].get('hwnd')==hwnd,'External setup cannot target an unverified/foreign window')
        atomic_new(output/'diagnostic-setup-hold.json',record)
        release_path=output/'diagnostic-setup-release.json'
        while not release_path.exists():
            require(time.monotonic()<deadline and still_allowed(),'Original thirty-second checkpoint budget/process/control no longer permits setup')
            time.sleep(.01)
        require(not release_path.is_symlink() and release_path.stat().st_size<=65536,'Setup release must be a bounded ordinary file')
        raw=release_path.read_bytes();release=parse_json_bytes(raw)
        require(time.monotonic()<deadline and still_allowed(),'Setup release arrived after original checkpoint bound')
        require(type(release) is dict and all(release.get(k)==v and type(release[k]) is type(v) for k,v in identity.items()),'Setup release belongs to another checkpoint/PID/HWND')
        require(type(release.get('schemaVersion')) is int and release['schemaVersion']==1 and release.get('kind')=='diagnostic-setup-release','Wrong external setup release contract')
        require(release.get('action')=='operator-focus-only' and release.get('completed') is True,'External setup action was not completed as declared')
        record.update(releaseSha256=sha256_bytes(raw),release=release,after=window_facts(pid,hwnd))
        require(record['after']['status']=='controlled','Actual window is not foreground-controlled after external setup')
        record['status']='released';return record
    except Exception as error:record.update(status='failed',failure=str(error));raise
    finally:
        record['completedAtUtc']=datetime.now(timezone.utc).isoformat();atomic_new(output/'diagnostic-setup-outcome.json',record)
