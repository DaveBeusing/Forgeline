"""CPU-only supervisor failure, readiness and immutable-output checks."""
import json,subprocess,tempfile,time,unittest
from pathlib import Path
from unittest.mock import patch
import owner_trace as o
from baseline_contracts import sha256_bytes
class Child:
    def __init__(self,alive=True):self.alive=alive;self.returncode=None if alive else 0;self.calls=[]
    def poll(self):return None if self.alive else self.returncode
    def terminate(self):self.calls.append('terminate');raise OSError('terminate denied')
    def kill(self):self.calls.append('kill');raise OSError('kill denied')
    def wait(self,timeout):self.calls.append(('wait',timeout));raise subprocess.TimeoutExpired('tracer',timeout)
class OwnerTraceTests(unittest.TestCase):
    def test_cleanup_retains_both_errors_and_bounded_survivor(self):
        child=Child();result=o.terminate_bounded(child)
        self.assertIn('tracer-still-alive-after-bounded-cleanup',result);self.assertEqual(child.calls,['terminate',('wait',5),'kill',('wait',5)])
    def test_exited_tracer_does_not_receive_cleanup_signals(self):
        child=Child(False);self.assertEqual(o.terminate_bounded(child),[]);self.assertEqual(child.calls,[])
    def test_invalid_output_under_known_payload_never_mutates_inventory(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);client=root/'client.exe';client.write_bytes(b'client');target=root/'forbidden'
            with self.assertRaises(ValueError):o.profile_owner(str(client),'missing','missing','missing','missing','missing',root,target)
            self.assertFalse(target.exists());self.assertEqual(client.read_bytes(),b'client')
    def context(self,deadline):
        checkpoint={'runId':'actual','sourceSha':'a'*40,'ownerThread':{'nativeThreadId':100,'processId':123},'adapterIdentity':{'processStartedFileTimeUtc':200}}
        return dict(checkpoint=checkpoint,checkpointBytes=b'actual-bytes',processId=123,commandDeadline=deadline,stillAlive=lambda:True,fail=self.failures.append)
    def test_readiness_cannot_reset_expired_outer_command_deadline(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.failures=[];session=o.OwnerTraceSession('tool',root,root/'control','00:00:30',1024);session.control.mkdir();session.context=self.context(time.monotonic()-1)
            with patch.object(session,'_start_collect') as launch:session._await_ready();launch.assert_not_called()
            self.assertTrue(self.failures);self.assertIn('original command bound',self.failures[0])
    def test_replayed_ready_session_fails_before_tracer_launch(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.failures=[];session=o.OwnerTraceSession('tool',root,root/'control','00:00:30',1024);session.control.mkdir();session.context=self.context(time.monotonic()+1)
            value=dict(kind='actual-owner-trace-ready',qualified=False,runId='replayed',processId=123,sourceSha='a'*40,checkpointSha256=sha256_bytes(b'actual-bytes'),ownerThread=session.context['checkpoint']['ownerThread'])
            (session.control/'trace-ready.json').write_text(json.dumps(value))
            with patch.object(session,'_start_collect') as launch:session._await_ready();launch.assert_not_called()
            self.assertIn('another source/process',self.failures[0])
    def test_cleanup_errors_do_not_suppress_actual_command_record(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);self.failures=[];session=o.OwnerTraceSession('tool',root,root/'control','00:00:30',1024);session.context=self.context(time.monotonic()-1);session.child=Child();session.command=dict(argv=['literal','collect'],startedAtUtc=o.now(),outputs={})
            session.stdout=(root/'collect.stdout').open('wb');session.stdout.write(b'partial stdout');session.stderr=(root/'collect.stderr').open('wb');session.stderr.write(b'partial stderr')
            session._collect();record=json.loads((root/'collect.command.json').read_bytes())
            self.assertTrue(record['cleanup']);self.assertIsNone(record['exitCode']);self.assertEqual(record['outputs']['stdout']['sha256'],sha256_bytes(b'partial stdout'));self.assertTrue(self.failures)
if __name__=='__main__':unittest.main()
