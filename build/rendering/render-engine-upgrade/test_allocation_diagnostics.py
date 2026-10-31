import base64,copy,struct,unittest
from allocation_diagnostics import correlate,positive_sample
from baseline_contracts import sha256_bytes,canonical

class AllocationClockTests(unittest.TestCase):
    def setUp(self):
        self.trace=dict(status='diagnostic-pass',expectedPid=10,clock=dict(available=True,format='pinned-nettrace-v6-raw-header',tickFrequency=1000000,syncTicks=1000000,utcSystemTimeBytesBase64=base64.b64encode(struct.pack('<8H',2026,10,5,30,12,0,0,0)).decode()),events=[dict(processId=10,nativeThreadId=12,timestampQpc=1010000)])
        self.owner=dict(sourceSha='a'*40,runtimeFingerprint='b'*64,adapterIdentity=dict(sessionId='c'*32,processStartedFileTimeUtc=555),ownerThread=dict(processId=10,nativeThreadId=12,managedThreadId=99),clockAnchor=dict(beforeTick=1000000,afterTick=1000010,frequencyHz=1000000,utc='2026-10-30T12:00:00+00:00'),frames=[dict(index=0,startTick=1005000,endTick=1020000)])
        header=bytearray(64);header[:8]=b'Nettrace';struct.pack_into('<4I',header,8,0,6,0,(1<<24)|40);header[24:40]=base64.b64decode(self.trace['clock']['utcSystemTimeBytesBase64']);struct.pack_into('<qq',header,40,1000000,1000000)
        self.trace['traceLength']=64;self.trace['traceSha256Before']=self.trace['traceSha256After']='d'*64
        self.trace['clock'].update(headerBytesBase64=base64.b64encode(header).decode(),headerSha256=sha256_bytes(header))
        self.trace['producerBinding']=dict(ownerSha256=sha256_bytes(canonical(self.owner)),traceSha256='d'*64,sourceSha='a'*40,runtimeFingerprint='b'*64,sessionId='c'*32,processId=10,processStartedFileTimeUtc=555)
    def check(self):return correlate(self.trace,self.owner,source_sha='a'*40,runtime_fingerprint='b'*64,producer_session='c'*32)
    def test_raw_tick_join_uses_native_thread_not_managed_thread(self):
        self.assertEqual(self.check()['events'][0]['frameIndex'],0)
        self.trace['events'][0]['nativeThreadId']=99
        self.assertEqual(self.check()['events'],[])
    def test_unknown_header_mismatched_frequency_source_process_and_anchor_reject(self):
        for mutation in ('unknown','frequency','bool','source','process','anchor','session'):
            with self.subTest(mutation=mutation):
                originalTrace,originalOwner=copy.deepcopy(self.trace),copy.deepcopy(self.owner)
                if mutation=='unknown':self.trace['clock']['available']=False
                elif mutation=='frequency':self.trace['clock']['tickFrequency']=2000000
                elif mutation=='bool':self.trace['clock']['tickFrequency']=True
                elif mutation=='source':self.owner['sourceSha']='d'*40
                elif mutation=='process':self.trace['events'][0]['processId']=11
                elif mutation=='session':self.owner['adapterIdentity']['sessionId']='e'*32
                else:self.owner['clockAnchor']['utc']='2026-10-30T12:10:00+00:00'
                self.assertEqual(self.check()['status'],'unavailable')
                self.trace,self.owner=originalTrace,originalOwner
    def test_ambiguous_half_open_boundary_is_not_forced_into_frame(self):
        self.trace['events'][0]['timestampQpc']=1020000
        r=self.check()['events'][0];self.assertEqual(r['status'],'ambiguous-boundary');self.assertIsNone(r['frameIndex'])
    def test_missing_original_trace_binding_is_unavailable(self):
        self.trace.pop('producerBinding');self.assertEqual(self.check()['status'],'unavailable')
    def test_legacy_null_extended_amount_uses_preserved_original_amount(self):
        self.assertTrue(positive_sample(dict(family='GCAllocationTick',payload=dict(allocationAmount64=None,AllocationAmount=100))))
        self.assertFalse(positive_sample(dict(family='GCAllocationTick',payload=dict(allocationAmount64=None,AllocationAmount=None))))
    def test_tampered_raw_header_summary_is_unavailable(self):
        self.trace['clock']['syncTicks']+=1;self.assertEqual(self.check()['status'],'unavailable')
    def test_outside_window_retained(self):
        self.trace['events'][0]['timestampQpc']=1030000
        self.assertEqual(self.check()['events'][0]['status'],'outside-window')

if __name__=='__main__':unittest.main()
