"""Query lock excludes external setup without weakening actual query sequencing."""
import json,tempfile,threading,unittest
from pathlib import Path
from unittest.mock import patch
import adapter_handshake as h
class HandshakeLockTests(unittest.TestCase):
    def test_queries_are_serialized_but_external_setup_allows_sensor_queries(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'checkpoint.json').write_text(json.dumps({'adapterIdentity':{'processId':123}}));sensor=root/'sensor.exe';sensor.write_bytes(b'actual-tool')
            lock=threading.Lock();calls=[]
            def query(*args,**kwargs):
                self.assertTrue(lock.locked());calls.append('cuda');return {}
            def observe(*args):
                self.assertTrue(lock.locked());calls.append('process');return {}
            def record(argv,*args):
                self.assertTrue(lock.locked());calls.append('version' if '--version' in argv else 'sensor');return (b'version' if '--version' in argv else b'raw'),{}
            def external(proof):
                self.assertFalse(lock.locked());self.assertFalse((root/'binding.json').exists())
                sampled=[]
                def sample():
                    with lock:sampled.append('actual-concurrent-observation')
                worker=threading.Thread(target=sample);worker.start();worker.join(timeout=1)
                self.assertFalse(worker.is_alive());self.assertEqual(len(sampled),1)
            with patch.object(h,'observe_process',side_effect=observe),patch.object(h,'query_cuda_identity_bounded',side_effect=query),patch.object(h,'resolve_executable',return_value=str(sensor)),patch.object(h,'run_record',side_effect=record),patch.object(h,'parse_inventory',return_value=[]),patch.object(h,'bind_adapter',return_value={'exactPhysicalMappingAvailable':True}):
                h.complete_checkpoint(root,{'processId':123},'driver.dll',str(sensor),external,query_lock=lock)
            self.assertEqual(calls,['process','cuda','version','sensor','process']);self.assertTrue((root/'binding.json').is_file())
if __name__=='__main__':unittest.main()
