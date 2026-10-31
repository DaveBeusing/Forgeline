import base64,dataclasses,json,tempfile,time,unittest
from datetime import datetime,timedelta,timezone
from pathlib import Path
from unittest.mock import patch
from baseline_contracts import ContractError,sha256_bytes
from provider import parse_inventory
from sensor_journal import SensorJournal,SensorJournalReceipt
from post_window_control import make_ack,publish_ack
RAW=b'GPU-aaaa-bbbb,00000000:30:00.0,GPU,596.53,41,180,11.08,0x0000000000000004\n'
def encoded(v):return json.dumps(v,separators=(',',':')).encode()
class PostWindowControlTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name)
        self.now=datetime.now(timezone.utc);at=lambda ms:(self.now+timedelta(milliseconds=ms)).isoformat()
        self.checkpoint=encoded(dict(sourceSha='a'*40,runtimeFingerprint='b'*64,runId='run',adapterIdentity=dict(processId=42),nativeWindowHandle=43))
        self.binding=dict(exactPhysicalMappingAvailable=True,nativeManifestSha256=sha256_bytes(self.checkpoint),uuid='GPU-aaaa-bbbb',pciBusId='00000000:30:00.0')
        self.ready=encoded(dict(schemaVersion=1,kind='ordinary-post-window-control-ready',qualified=False,sourceSha='a'*40,runtimeFingerprint='b'*64,runId='run',checkpointSha256=sha256_bytes(self.checkpoint),processId=42,nativeWindowHandle=43,lastSubmission=dict(submission=512,submittedFence=600,generation=1,resourceGeneration=1),sampleCount=512,lastMeasuredEndTick=100,lastCycleEndTick=101,ownerCycleAvailable=True,endpointKind='owner-cycle-v1',sealedEndTick=102,frequencyHz=10000000,sealedAtUtc=at(-300),caseIdentity=dict(kind='actual-live-session',caseKey='actual',simulationSession=1,sessionOriginFingerprint='d'*64,recipeAvailable=False,recipeFingerprint=None,profileFingerprint=sha256_bytes(b'{}'),profileInputBytesBase64=base64.b64encode(b'{}').decode())))
        self.sample=dict(acquisitionStartedAtUtc=at(-200),observedAtUtc=at(-100),devices=parse_inventory(RAW),postWindowReadySha256=sha256_bytes(self.ready),command=dict(exitCode=0,timedOut=False),window=dict(status='controlled',requestedPid=42,ownerPid=42,hwnd=43,foregroundHwnd=43,foregroundOwnerPid=42,visible=True,iconic=False,reasons=[],candidates=[43]))
    def receipt(self,sample=None):
        directory=self.root/str(len(list(self.root.iterdir())))
        with SensorJournal(directory) as journal:return journal.append(sample or self.sample,RAW)
    def ack(self,receipt,**kw):return make_ack(self.ready,self.checkpoint,receipt,command_deadline=kw.get('deadline',time.monotonic()+5),still_allowed=kw.get('alive',lambda:True),binding=kw.get('binding',self.binding))
    def test_success_binds_actual_durable_prefix_and_raw_provider(self):
        receipt=self.receipt();ack=self.ack(receipt)
        self.assertEqual(ack['providerUuid'],self.binding['uuid']);self.assertEqual(base64.b64decode(ack['rowBytesBase64']),receipt.row_bytes)
        self.assertEqual(ack['prefixReceiptSha256'],sha256_bytes((receipt.journal_path.parent/'latest.json').read_bytes()))
    def test_untyped_caller_receipt_is_not_durability_proof(self):
        with self.assertRaisesRegex(ContractError,'source-owned'):self.ack(dict(rowBytes=b'fake'))
    def test_changed_actual_journal_or_short_prefix_rejected(self):
        for raw in (b'changed',b''):
            receipt=self.receipt();receipt.journal_path.write_bytes(raw)
            with self.assertRaises(ContractError):self.ack(receipt)
    def test_forged_counts_hash_and_bool_alias_rejected(self):
        for key,value in [('rows',True),('prefixBytes',True),('prefixBytes',1),('prefixSha256','z'*64),('kind','caller-label')]:
            receipt=self.receipt();prefix=json.loads(receipt.prefix_receipt_bytes);prefix[key]=value
            with self.subTest(key=key,value=value),self.assertRaises(ContractError):self.ack(dataclasses.replace(receipt,prefix_receipt_bytes=encoded(prefix)))
    def test_failed_fsync_never_produces_receipt_or_ack(self):
        directory=self.root/'failed';journal=SensorJournal(directory)
        try:
            with patch('sensor_journal.os.fsync',side_effect=OSError('disk failed')):
                with self.assertRaises(OSError):journal.append(self.sample,RAW)
            self.assertIsNotNone(journal.failed);self.assertFalse((directory/'ack.json').exists())
        finally:journal.close()
    def test_query_already_inflight_or_late_cannot_ack(self):
        for mutation in [dict(postWindowReadySha256='0'*64),dict(acquisitionStartedAtUtc=(self.now-timedelta(seconds=1)).isoformat()),dict(observedAtUtc=(self.now+timedelta(seconds=6)).isoformat())]:
            with self.subTest(mutation=mutation),self.assertRaises(ContractError):self.ack(self.receipt(dict(self.sample,**mutation)))
    def test_foreign_hidden_minimized_or_foreground_loss_cannot_ack(self):
        for key,value in [('requestedPid',99),('foregroundHwnd',99),('foregroundOwnerPid',99),('visible',False),('iconic',True),('reasons',['lost']),('candidates',[])]:
            with self.subTest(key=key),self.assertRaises(ContractError):self.ack(self.receipt(dict(self.sample,window=dict(self.sample['window'],**{key:value}))))
    def test_original_deadline_and_process_loss_cannot_ack(self):
        receipt=self.receipt()
        for kw in [dict(deadline=time.monotonic()-1),dict(alive=lambda:False)]:
            with self.assertRaises(ContractError):self.ack(receipt,**kw)
    def test_foreign_provider_binding_cannot_ack(self):
        for mutation in [dict(uuid='GPU-ffff'),dict(nativeManifestSha256='0'*64),dict(exactPhysicalMappingAvailable=False)]:
            with self.assertRaises(ContractError):self.ack(self.receipt(),binding=dict(self.binding,**mutation))
    def test_prefix_cannot_substitute_different_row_or_raw_values(self):
        receipt=self.receipt();prefix=json.loads(receipt.prefix_receipt_bytes);prefix['lastObservation']['command']['exitCode']=False
        with self.assertRaises(ContractError):self.ack(dataclasses.replace(receipt,prefix_receipt_bytes=encoded(prefix)))
        sample=dict(self.sample,devices=[dict(self.sample['devices'][0],clockMHz=999)])
        with self.assertRaisesRegex(ContractError,'raw query'):self.ack(self.receipt(sample))
    def test_supervisor_does_not_reuse_query_started_before_ready(self):
        import threading
        from post_window_control import PostWindowSupervisor
        folder=self.root/'post';folder.mkdir();wake=threading.Event()
        supervisor=PostWindowSupervisor(folder,self.checkpoint,self.binding,time.monotonic()+5,lambda:True,wake)
        old=supervisor.begin_query();self.assertIsNone(old)
        (folder/'ready.json').write_bytes(self.ready);supervisor.poll();self.assertTrue(wake.is_set())
        supervisor.finish_query(old,self.receipt());self.assertFalse(supervisor.completed)
        pending=supervisor.begin_query();self.assertEqual(pending,self.ready)
        supervisor.finish_query(pending,self.receipt());self.assertTrue(supervisor.completed)
        self.assertTrue((folder/'ack.json').exists());self.assertIsNone(supervisor.begin_query())
    def test_supervisor_rejects_mutated_ready_and_process_loss(self):
        import threading
        from post_window_control import PostWindowSupervisor
        folder=self.root/'post';folder.mkdir()
        supervisor=PostWindowSupervisor(folder,self.checkpoint,self.binding,time.monotonic()+5,lambda:True,threading.Event())
        (folder/'ready.json').write_bytes(self.ready);supervisor.poll()
        changed=json.loads(self.ready);changed['sampleCount']=128;(folder/'ready.json').write_bytes(encoded(changed))
        with self.assertRaisesRegex(ContractError,'changed'):supervisor.poll()
        supervisor=PostWindowSupervisor(folder,self.checkpoint,self.binding,time.monotonic()+5,lambda:False,threading.Event())
        with self.assertRaisesRegex(ContractError,'expired'):supervisor.poll()
    def test_seal_and_row_cannot_each_restart_existing_five_second_budget(self):
        ready=json.loads(self.ready);ready['sealedEndTick']=ready['lastCycleEndTick']+4*ready['frequencyHz'];ready['sealedAtUtc']=(self.now-timedelta(seconds=2)).isoformat()
        self.ready=encoded(ready);sample=dict(self.sample,postWindowReadySha256=sha256_bytes(self.ready))
        with self.assertRaisesRegex(ContractError,'five-second'):self.ack(self.receipt(sample))
    def test_retained_consumer_joins_final_frame_and_original_command(self):
        from post_window_control import validate_retained
        folder=self.root/'post';folder.mkdir();(folder/'ready.json').write_bytes(self.ready)
        self.binding['commandContext']=dict(startedAtUtc=(self.now-timedelta(seconds=1)).isoformat(),timeoutSeconds=600)
        with SensorJournal(self.root/'sensors') as journal:receipt=journal.append(self.sample,RAW)
        publish_ack(folder,self.ready,self.checkpoint,receipt,command_deadline=time.monotonic()+5,still_allowed=lambda:True,binding=self.binding)
        ready=json.loads(self.ready);kw=dict(last_submission=ready['lastSubmission'],last_measured_end=100,last_cycle_end=101,case_identity=ready['caseIdentity'],sample_count=512,command_completed_at=(self.now+timedelta(seconds=1)).isoformat())
        self.assertEqual(validate_retained(folder,self.checkpoint,self.binding,**kw)['kind'],'verified-ordinary-post-window-control')
        for mutation in [dict(last_submission=dict(ready['lastSubmission'],submittedFence=601)),dict(last_cycle_end=102),dict(sample_count=128)]:
            with self.subTest(mutation=mutation),self.assertRaisesRegex(ContractError,'final frame'):validate_retained(folder,self.checkpoint,self.binding,**dict(kw,**mutation))
if __name__=='__main__':unittest.main()
