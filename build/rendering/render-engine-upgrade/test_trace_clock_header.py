import base64,copy,struct,unittest
from trace_clock_header import decode_clock_header
from allocation_diagnostics import correlate
from baseline_contracts import sha256_bytes,canonical

def legacy_header(pid=10,frequency=1000000,rate=0):
    return (b'Nettrace'+struct.pack('<i',20)+b'!FastSerialization.1'+b'\x05\x05\x01'+
        struct.pack('<iii',4,4,5)+b'Trace\x06'+struct.pack('<8H',2026,10,5,30,12,0,0,0)+
        struct.pack('<qqiiii',1000000,frequency,8,pid,24,rate)+b'\x06')

class TraceClockHeaderTests(unittest.TestCase):
    def test_documented_v4_exact_envelope_and_zero_historical_sampling_rate(self):
        raw=legacy_header();self.assertEqual(len(raw),102)
        value=decode_clock_header(raw,102,10)
        self.assertEqual(value['format'],'pinned-nettrace-fastserialization-trace-v4')
        self.assertEqual(value['headerLength'],102);self.assertEqual(value['traceProcessId'],10)
        self.assertEqual(value['tickFrequency'],1000000);self.assertEqual(value['syncTicks'],1000000)
        self.assertEqual(value['expectedCpuSamplingRate'],0)
    def test_all_v4_magic_type_tag_length_version_payload_and_footer_guards(self):
        edits=[(0,'B',0),(8,'i',19),(12,'B',0),(32,'B',4),(33,'B',4),(34,'B',0),
            (35,'i',5),(39,'i',3),(43,'i',4),(47,'B',0),(52,'B',0),(101,'B',0),
            (53,'H',0),(55,'H',13),(57,'H',7),(59,'H',32),(61,'H',24),(63,'H',60),(65,'H',60),(67,'H',1000),
            (69,'q',-1),(77,'q',0),(77,'q',-1),(85,'i',16),(89,'i',0),(89,'i',11),(93,'i',0),(97,'i',-1)]
        for offset,fmt,value in edits:
            raw=bytearray(legacy_header());struct.pack_into('<'+fmt,raw,offset,value)
            with self.subTest(offset=offset,value=value),self.assertRaises(ValueError):decode_clock_header(bytes(raw),102,10)
    def test_every_truncation_overlong_prefix_and_actual_file_bound_reject(self):
        raw=legacy_header()
        for n in range(len(raw)):
            with self.subTest(length=n),self.assertRaises(ValueError):decode_clock_header(raw[:n],102,10)
        with self.assertRaises(ValueError):decode_clock_header(raw+b'\0',103,10)
        with self.assertRaises(ValueError):decode_clock_header(raw,101,10)
    def test_existing_v6_clock_contract_remains_supported(self):
        raw=bytearray(64);raw[:8]=b'Nettrace';struct.pack_into('<4I',raw,8,0,6,0,(1<<24)|40)
        raw[24:40]=struct.pack('<8H',2026,10,5,30,12,0,0,0);struct.pack_into('<qq',raw,40,1000000,1000000)
        value=decode_clock_header(bytes(raw),64,10);self.assertEqual(value['format'],'pinned-nettrace-v6-raw-header')
        struct.pack_into('<I',raw,20,(1<<24)|41)
        with self.assertRaises(ValueError):decode_clock_header(bytes(raw),64,10)
    def test_legacy_clock_support_does_not_grant_missing_producer_binding(self):
        raw=legacy_header();clock=decode_clock_header(raw,102,10)
        clock.update(available=True,headerBytesBase64=base64.b64encode(raw).decode(),headerSha256=sha256_bytes(raw))
        trace=dict(status='diagnostic-pass',expectedPid=10,clock=clock,traceLength=102,traceSha256Before='d'*64,traceSha256After='d'*64,
            events=[dict(processId=10,nativeThreadId=12,timestampQpc=1010000)])
        owner=dict(sourceSha='a'*40,runtimeFingerprint='b'*64,adapterIdentity=dict(sessionId='c'*32,processStartedFileTimeUtc=555),
            ownerThread=dict(processId=10,nativeThreadId=12,managedThreadId=99),clockAnchor=dict(beforeTick=1000000,afterTick=1000010,frequencyHz=1000000,utc='2026-10-30T12:00:00Z'),
            frames=[dict(index=0,startTick=1005000,endTick=1020000)])
        def check():return correlate(trace,owner,source_sha='a'*40,runtime_fingerprint='b'*64,producer_session='c'*32)
        self.assertEqual(check()['status'],'unavailable')
        trace['producerBinding']=dict(ownerSha256=sha256_bytes(canonical(owner)),traceSha256='d'*64,sourceSha='a'*40,runtimeFingerprint='b'*64,sessionId='c'*32,processId=10,processStartedFileTimeUtc=555)
        self.assertEqual(check()['events'][0]['frameIndex'],0)
        trace['clock']['traceProcessId']=11
        self.assertEqual(check()['status'],'unavailable')

if __name__=='__main__':unittest.main()
