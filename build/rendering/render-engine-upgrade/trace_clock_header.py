"""Original pinned v4/v6 trace clock prefixes; never the offline parser machine's clock."""
import base64,struct
from datetime import datetime,timezone
from baseline_contracts import require

def decode_clock_header(raw,trace_length,expected_pid):
    require(type(raw) is bytes and 12<=len(raw)<=512 and type(trace_length) is int and trace_length>=len(raw),'Original trace clock prefix length invalid')
    require(type(expected_pid) is int and expected_pid>0 and raw[:8]==b'Nettrace','Actual trace process/magic unavailable')
    def utc_fields(data):
        fields=struct.unpack('<8H',data)
        at=datetime(fields[0],fields[1],fields[3],fields[4],fields[5],fields[6],fields[7]*1000,tzinfo=timezone.utc)
        require(fields[2]==(at.weekday()+1)%7,'SYSTEMTIME weekday differs from original UTC date')
    if struct.unpack_from('<i',raw,8)[0]==20:
        cursor=0
        def take(n):
            nonlocal cursor
            require(n>=0 and cursor+n<=len(raw),'Truncated original trace clock prefix')
            value=raw[cursor:cursor+n];cursor+=n;return value
        def i32():return struct.unpack('<i',take(4))[0]
        def i64():return struct.unpack('<q',take(8))[0]
        require(take(8)==b'Nettrace' and i32()==20 and take(20)==b'!FastSerialization.1','Invalid FastSerialization clock magic')
        require(take(3)==b'\x05\x05\x01','Invalid Trace/type/null tags')
        require(i32()==4 and i32()==4,'Unsupported Trace/minimum-reader version')
        require(i32()==5 and take(5)==b'Trace' and take(1)==b'\x06','Invalid Trace type length/name/footer')
        utc_raw=take(16);utc_fields(utc_raw);sync,frequency=i64(),i64()
        pointer,pid,processors,rate=i32(),i32(),i32(),i32()
        require(take(1)==b'\x06' and cursor==len(raw)==102,'Invalid Trace payload length/footer')
        require(pointer in (4,8) and pid==expected_pid and pid>0,'Original trace pointer/PID differs')
        require(processors>0 and rate>=0,'Unsupported auxiliary metadata for this capture scope; zero historical sampling rate is valid')
        result=dict(format='pinned-nettrace-fastserialization-trace-v4',headerLength=cursor,traceProcessId=pid,pointerSize=pointer,processorCount=processors,expectedCpuSamplingRate=rate)
    else:
        require(len(raw)==64,'Original v6 header must be64bytes')
        reserved,major,minor,block=struct.unpack_from('<4I',raw,8)
        require(reserved==0 and major==6 and block>>24==1 and block&0xffffff>=40 and 24+(block&0xffffff)<=trace_length,'Original v6 TraceBlock length/version invalid')
        sync,frequency=struct.unpack_from('<qq',raw,40);utc_raw=raw[24:40];utc_fields(utc_raw)
        result=dict(format='pinned-nettrace-v6-raw-header',headerLength=64)
    require(sync>=0 and frequency>0,'Original QPC origin/frequency unavailable')
    result.update(syncTicks=sync,tickFrequency=frequency,utcSystemTimeBytesBase64=base64.b64encode(utc_raw).decode())
    return result
